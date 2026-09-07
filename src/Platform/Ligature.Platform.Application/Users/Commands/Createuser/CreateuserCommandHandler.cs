using Ligature.Platform.Application.Abstractions;
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

    public CreateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserTokenRepository userTokenRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService)
    {
        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _userTokenRepository = userTokenRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
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

        // Transaction scope is handler-owned for this implementation phase.
        //
        // The catalogue assigns it to the pipeline (behaviour 6), and that will
        // be reconsidered when audit emission arrives — behaviour 7 requires
        // audit events to be written INSIDE the command's transaction, which a
        // pipeline behaviour cannot do while the handler opens and closes the
        // transaction internally. Moving it now, purely to support a capability
        // that does not exist yet, would be speculative. Recorded here so
        // today's arrangement is not mistaken for the final architecture.
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

                    // TODO — USR-C1 step 8. Emit UserCreated, IdentityCreated
                    // and TokenIssued, each carrying the CALLER's ActorSnapshot
                    // (the administrator, not the created user), inside this
                    // transaction: "if the business write committed, the audit
                    // write committed" (behaviour 7, inv. 16). Deferred with
                    // the Audit capability. Note that a full snapshot needs
                    // Username, IdentityProvider, SubjectId and AuthorizingRole,
                    // none of which IExecutionContext carries today.

                    // TODO — USR-C1 step 9. Enqueue the activation
                    // notification. Deferred with the Notification capability.
                    // Whatever delivers it must reckon with UT7: an outbox row
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