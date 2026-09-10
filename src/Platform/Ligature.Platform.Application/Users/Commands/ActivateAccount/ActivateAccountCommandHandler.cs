using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.ActivateAccount;

/// <summary>
/// CRD-C1 — ActivateAccount.
///
/// Creates the credential row that makes authentication possible (inv. 15):
/// until it exists there is nothing to check a password against, which is why
/// no PendingActivation status exists to drift out of step with reality.
/// </summary>
public sealed class ActivateAccountCommandHandler
    : ICommandHandler<ActivateAccountCommand, ActivateAccountResult>
{
    /// <summary>
    /// One message for every rejection. An unknown token id, a wrong secret, an
    /// already-used token, an invalidated one and an expired one are
    /// indistinguishable to the caller — otherwise the activation endpoint
    /// answers questions about tokens that it should not answer.
    /// </summary>
    private const string InvalidToken = "The activation token is not valid.";

    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly IUserTokenService _userTokenService;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly ITokenBearerEstablisher _tokenBearerEstablisher;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEvents _auditEvents;

    public ActivateAccountCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserTokenRepository userTokenRepository,
        IUserTokenService userTokenService,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher,
        ICredentialRepository credentialRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        ITokenBearerEstablisher tokenBearerEstablisher,
        IExecutionContext executionContext,
        IAuditEvents auditEvents)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userTokenRepository = userTokenRepository;
        _userTokenService = userTokenService;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
        _credentialRepository = credentialRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _tokenBearerEstablisher = tokenBearerEstablisher;
        _executionContext = executionContext;
        _auditEvents = auditEvents;
    }

    public async Task<ActivateAccountResult> Handle(
        ActivateAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;

        // A malformed token is just another invalid token: same message, so the
        // shape of what was submitted is not reflected back either.
        var presented = _userTokenService.Parse(command.TokenPlainText)
            ?? throw new BusinessRuleViolationException(InvalidToken);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // Step 1. The conditional UPDATE decides, and it runs inside
                // this transaction — so a later failure, a password below the
                // policy floor most likely, rolls the consumption back and the
                // token stays usable. Burning an activation token on a rejected
                // password would leave the user permanently unable to activate.
                var identityId = await _userTokenRepository.TryConsumeAsync(
                    presented.TokenId,
                    _userTokenService.Hash(presented.Secret),
                    now,
                    ct);

                if (identityId is null)
                    throw new BusinessRuleViolationException(InvalidToken);

                // Step 1b. The consumption above is the authentication: it
                // proved the bearer holds a live token for THIS identity. The
                // identity it returned is therefore the sole authority for who
                // is acting, and it is passed straight through. Resolving the
                // actor any other way — a second token read, a walk from the
                // user back to an identity — would let the actor drift from
                // the bearer that was actually authenticated (AUD-D28).
                //
                // Everything after this point runs under an established
                // caller, which is what lets the three records below carry an
                // authenticated origin. A false result means the identity or
                // its user has gone missing inside this very transaction, so
                // it collapses into the same opaque rejection as every other
                // invalid activation.
                if (!await _tokenBearerEstablisher.EstablishAsync(identityId, ct))
                    throw new BusinessRuleViolationException(InvalidToken);

                // Read back rather than derived: the Subject ref must be the
                // user of the established actor, not a second lookup.
                var subject = _executionContext.UserId;

                _auditEvents.Emit("TokenConsumed", version: 1)
                    .Primary("Token", presented.TokenId.Value)
                    .Ref("Identity", identityId.Value, role: "Target")
                    .Ref("User", subject.Value, role: "Subject")
                    .WithPayload(new
                    {
                        // What consumed it, not what it claimed to be: the
                        // consumption query matches on id, secret and
                        // liveness, so the token's own type is not something
                        // this command has verified.
                        consumedBy = "Activation",
                        consumedAt = now,
                    });

                // Step 2. PasswordMinLength is a FLOOR — max(tenant, baseline)
                // — resolved at evaluation time, so a tightened baseline
                // applies immediately (inv. 30, 31).
                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                // Step 3. No reuse check on activation: there is no history to
                // check against yet. CRD-C3 adds it for reset.
                if (command.NewPassword is null
                    || command.NewPassword.Length < policy.PasswordMinLength)
                {
                    throw new BusinessRuleViolationException(
                        "The password must be at least "
                        + $"{policy.PasswordMinLength} characters.");
                }

                // Step 4. The adapter owns the algorithm and its parameters;
                // this handler never learns either.
                var hashed = _passwordHasher.Hash(command.NewPassword);

                // Steps 5 and 6. CR5 — credential and history are written in
                // the SAME transaction, "otherwise the reuse check has a
                // permanent hole".
                //
                // CreatedBy is the System actor because nobody is
                // authenticated. Attributing the row to the user being
                // activated would assert an authentication that did not happen;
                // that this person set their own password is an audit fact, not
                // a provenance column.
                // Hoisted so the record can name the row it describes; the
                // catalogue makes Credential the primary entity of PasswordSet.
                var credentialId = CredentialId.New();

                await _credentialRepository.AddAsync(
                    Credential.Create(
                        credentialId,
                        identityId,
                        IdentityType.Local,
                        hashed.Hash,
                        hashed.Algorithm,
                        passwordChangedAt: now,
                        mustChangePassword: false,
                        createdAt: now,
                        createdBy: User.SystemUserId),
                    ct);

                await _passwordHistoryRepository.AddAsync(
                    PasswordHistory.Create(
                        PasswordHistoryId.New(),
                        identityId,
                        hashed.Hash,
                        hashed.Algorithm,
                        createdAt: now),
                    ct);

                // The hash is the one thing that must never appear. The
                // algorithm marker is a version label, and whether a password
                // change is forced is a fact a reviewer needs.
                _auditEvents.Emit("PasswordSet", version: 1)
                    .Primary("Credential", credentialId.Value)
                    .Ref("Identity", identityId.Value, role: "Target")
                    .Ref("User", subject.Value, role: "Subject")
                    .WithPayload(new
                    {
                        algorithm = hashed.Algorithm,
                        mustChangePassword = false,
                        setBy = "Activation",
                    });

                // Shape None in the catalogue: the fact that it happened, to
                // whom, and by whom is the whole record. There is no state to
                // describe that the other two have not already described.
                _auditEvents.Emit("AccountActivated", version: 1)
                    .Primary("Identity", identityId.Value)
                    .Ref("User", subject.Value, role: "Subject");

                return new ActivateAccountResult(identityId);
            },
            cancellationToken);
    }
}
