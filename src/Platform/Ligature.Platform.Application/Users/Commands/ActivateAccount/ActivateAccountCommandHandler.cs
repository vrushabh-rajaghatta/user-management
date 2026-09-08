using Ligature.Platform.Application.Abstractions;
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

    public ActivateAccountCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserTokenRepository userTokenRepository,
        IUserTokenService userTokenService,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher,
        ICredentialRepository credentialRepository,
        IPasswordHistoryRepository passwordHistoryRepository)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userTokenRepository = userTokenRepository;
        _userTokenService = userTokenService;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
        _credentialRepository = credentialRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
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
                await _credentialRepository.AddAsync(
                    Credential.Create(
                        CredentialId.New(),
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

                // TODO — CRD-C1 step 7. Emit TokenConsumed, PasswordSet and
                // AccountActivated through IAuditWriter inside this
                // transaction, once the Audit capability exists
                // (docs/architecture.md section 6).

                return new ActivateAccountResult(identityId);
            },
            cancellationToken);
    }
}
