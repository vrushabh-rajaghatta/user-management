using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler
    : ICommandHandler<CreateUserCommand, CreateUserResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenService _userTokenService;
    private readonly IAuditEvents _auditEvents;

    public CreateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserTokenRepository userTokenRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService,
        IAuditEvents auditEvents)
    {
        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _userTokenRepository = userTokenRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
        _auditEvents = auditEvents;
    }

    /// <summary>
    /// USR-C1 steps 2–7. Step 1 (authorise: user.create, human-only) is done by
    /// the pipeline before this runs.
    ///
    /// The uniqueness checks below are affordances, not guarantees. AU3 and UI7
    /// are the authority, and PostgresExceptionTranslator turns their
    /// violations into the SAME exception type these throw — so a caller cannot
    /// tell whether the pre-check or the index rejected them, which is the
    /// point. Two concurrent creates both pass here and only one passes INSERT.
    ///
    /// ActorType is not passed in: User.CreateHuman forces Human, which is how
    /// inv. 17a's "Agent creation rejected" holds without a runtime check.
    /// </summary>
    public async Task<CreateUserResult> Handle(
    CreateUserCommand command,
    CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;

        var email =
            EmailAddress.Create(command.Email);

        if (await _userRepository
                .ExistsActiveHumanWithEmailAsync(
                    email,
                    cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "A user with this email address already exists.");
        }

        if (await _userIdentityRepository
                .ExistsWithUsernameAsync(
                    command.InitialUsername,
                    cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "A user identity with this username already exists.");
        }

        var userId = UserId.New();
        var identityId = UserIdentityId.New();

        var user = User.CreateHuman(
            userId,
            command.FirstName,
            command.LastName,
            command.DisplayName,
            command.Email,
            now,
            _executionContext.UserId
            );

        var identity = UserIdentity.CreateLocal(
            identityId,
            userId,
            ActorType.Human,
            command.InitialUsername,
            now,
            _executionContext.UserId
            );

        // The handler wraps its whole operation in IUnitOfWork, which saves
        // and translates known constraint violations. The transaction itself
        // is now opened by the pipeline (TransactionScopeBehavior) and this
        // call enlists in it: the work is flushed here, the audit rows follow
        // on the same transaction, and the pipeline commits — which is what
        // makes "if the business write committed, the audit write committed"
        // (inv. 16) true without this handler writing an audit row itself.
        var result =
            await _unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var policy =
                        await _securityPolicyResolver
                            .GetEffectiveSettingsAsync(
                                now,
                                ct);

                    // The id is minted first because the delivered token
                    // embeds it: CRD-C1 consumes by primary key.
                    var tokenId = UserTokenId.New();

                    // Only tokenMaterial.Hash is used. The plaintext has no
                    // consumer in this phase and is deliberately left with
                    // none: it lives for the duration of this method and is
                    // never persisted, returned or logged (UT7).
                    var tokenMaterial =
                        _userTokenService.Generate(tokenId);

                    var activationToken =
                        UserToken.Create(
                            tokenId,
                            identityId,
                            TokenType.Activation,
                            tokenMaterial.Hash,
                            now,
                            now + policy.ActivationTokenLifetime,
                            _executionContext.UserId);

                    await _userRepository.AddAsync(
                        user,
                        ct);

                    await _userIdentityRepository.AddAsync(
                        identity,
                        ct);

                    await _userTokenRepository.AddAsync(
                        activationToken,
                        ct);

                    // USR-C1 step 8 — declare the events. The pipeline writes
                    // them after this handler returns, still inside this
                    // transaction, under the CALLER's snapshot — the
                    // administrator, not the created user. Codes are verbatim
                    // from the catalogue; shapes and refs are what its rows
                    // declare, and behaviour 14 refuses anything else.
                    //
                    // The After objects use the catalogue's own path names
                    // (After.FirstName, After.Email ...) so that the PII paths
                    // anonymisation will later transform point at real keys.
                    _auditEvents.Emit("UserCreated", version: 1)
                        .Primary("User", userId.Value)
                        .WithAfter(new
                        {
                            user.FirstName,
                            user.LastName,
                            user.DisplayName,
                            Email = user.Email?.Value,
                        });

                    // SubjectId and IdentityProvider appear in After but are
                    // identifiers, not PII paths (AUD-D29).
                    _auditEvents.Emit("IdentityCreated", version: 1)
                        .Primary("Identity", identityId.Value)
                        .Ref("User", userId.Value, role: "Subject")
                        .WithAfter(new
                        {
                            identity.Username,
                            IdentityProvider = identity.IdentityProvider.Value,
                            identity.SubjectId,
                        });

                    // NEVER the token or its hash: tokenType and expiresAt are
                    // the whole payload, and the secret scan would refuse the
                    // rest.
                    _auditEvents.Emit("TokenIssued", version: 1)
                        .Primary("Token", tokenId.Value)
                        .Ref("Identity", identityId.Value, role: "Target")
                        .Ref("User", userId.Value, role: "Subject")
                        .WithPayload(new
                        {
                            tokenType = "Activation",
                            expiresAt = activationToken.ExpiresAt,
                        });

                    // TODO — USR-C1 step 9. Enqueue the activation
                    // notification. Deferred to the Notifications capability,
                    // which owns delivery — User Management does not own email
                    // infrastructure (docs/architecture.md section 8).
                    // Whatever delivers it must reckon with UT7: a queued row
                    // carrying the activation link necessarily carries the
                    // plaintext token, which is the exact disclosure that
                    // storing only a hash exists to prevent.

                    return new CreateUserResult(
                        userId,
                        identityId);
                },
                cancellationToken);

        return result;
    }
}