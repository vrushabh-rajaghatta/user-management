using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.AdminResetPassword;

/// <summary>
/// CRD-C5 — AdminResetPassword.
///
/// AN ADMINISTRATOR'S ACT, NOT THE SYSTEM'S. CRD-C2 issues the same kind of
/// token as the System actor because nobody authenticated; here a person
/// holding user.resetpassword did, so the token's CreatedBy and every audit
/// record name that administrator. That difference is the forensic signal UM
/// §6.5 exists to preserve: reviewing an account, it is immediately visible
/// which resets came from outside and which were pushed by an administrator.
///
/// NO ANTI-ENUMERATION DISCIPLINE. CRD-C2 must answer identically whether or
/// not an account exists; this caller is authenticated and authorised over
/// users, so an ineligible target is refused plainly.
///
/// EVERY CHECK BEFORE ANY WRITE. The pipeline has already opened the
/// transaction, so the eligibility reads run inside it, but all of them run
/// before the first write or declaration — a refused target leaves no token,
/// no credential change, no notification and no audit record.
///
/// WHAT IT DELIBERATELY DOES NOT DO:
///
///   - Serve a user who has no credential. That is the pending-activation state
///     (inv. 15), and issuing a reset token there would either produce a link
///     CRD-C3 refuses or turn this into a second activation path. A failed
///     activation mail is recovered by CRD-C7, ReissueActivationLink.
///   - Unlock the account. Issuing a reset is not completing one; CRD-C3's
///     password change clears the lockout, and CRD-C6 exists for unlocking.
///   - Revoke sessions. The catalogue's write set excludes user_session.
///   - Enforce MustChangePassword. This records the state; SES-C1 enforces
///     it, refusing the old password until a reset link replaces it
///     (docs/requirements.md, "SES-C1 — enforcing MustChangePassword at
///     sign-in").
/// </summary>
public sealed class AdminResetPasswordCommandHandler
    : ICommandHandler<AdminResetPasswordCommand, AdminResetPasswordResult>
{
    /// <summary>
    /// One message for every ineligible target. The wording is not a frozen
    /// contract; the refusal and its surface are.
    /// </summary>
    private const string NotEligible =
        "This user's password cannot be reset by an administrator.";

    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenService _userTokenService;
    private readonly IAuditEvents _auditEvents;
    private readonly INotificationEvents _notificationEvents;

    public AdminResetPasswordCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        ICredentialRepository credentialRepository,
        IUserTokenRepository userTokenRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService,
        IAuditEvents auditEvents,
        INotificationEvents notificationEvents)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(credentialRepository);
        ArgumentNullException.ThrowIfNull(userTokenRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(userTokenService);
        ArgumentNullException.ThrowIfNull(auditEvents);
        ArgumentNullException.ThrowIfNull(notificationEvents);

        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _credentialRepository = credentialRepository;
        _userTokenRepository = userTokenRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
        _auditEvents = auditEvents;
        _notificationEvents = notificationEvents;
    }

    public async Task<AdminResetPasswordResult> Handle(
        AdminResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Refused here, before any database work, on the ordinary 400 surface.
        // AdminPasswordResetIssued requires a reason, and without this check a
        // missing one would surface only when behaviour 7 assembled the record
        // — as an emission defect and a 500, for what is a caller's mistake.
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessRuleViolationException(
                "A reason is required to reset a user's password.");
        }

        var now = _clock.UtcNow;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // ---- Eligibility. Reads only; nothing below may be skipped.

                var subject = await _userRepository.FindAsync(command.UserId, ct);

                // Human only: an Agent authenticates with machine credentials
                // and the System actor cannot authenticate at all (UI8). An
                // email is required because it is the only delivery channel.
                if (subject is null
                    || subject.ActorType != ActorType.Human
                    || subject.Status != UserStatus.Active
                    || subject.Email is null)
                {
                    throw new BusinessRuleViolationException(NotEligible);
                }

                // Exactly one, and it must be active. More than one is refused
                // rather than resolved: no command creates a second local
                // identity, so two would be a state nobody designed, and
                // choosing between them would be a guess about whose mailbox
                // controls the account.
                var locals = await _userIdentityRepository
                    .FindLocalByUserIdAsync(subject.Id, ct);

                if (locals.Count != 1 || locals[0].Status != UserStatus.Active)
                    throw new BusinessRuleViolationException(NotEligible);

                var identity = locals[0];

                // No credential is the pending-activation state (inv. 15).
                // Refused, never answered with an activation token: that would
                // be a different command with different events, and
                // AdminPasswordResetIssued has no Credential to name.
                var credential = await _credentialRepository
                    .FindByIdentityAsync(identity.Id, ct);

                if (credential is null)
                    throw new BusinessRuleViolationException(NotEligible);

                // ---- Writes and declarations.

                // PasswordResetTokenLifetime is a CAP — min(tenant, baseline).
                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                // UT5 before the insert, for CRD-C2's reason: an expired but
                // unused token still holds UT4's slot. This also supersedes a
                // pending self-service link — one live reset link, whoever
                // asked for it.
                var superseded = await _userTokenRepository.InvalidatePriorAsync(
                    identity.Id, TokenType.PasswordReset, now, ct);

                var tokenId = UserTokenId.New();

                // Only the hash is persisted; the plaintext's one consumer is
                // the notification below (UT7). It is not in the result.
                var material = _userTokenService.Generate(tokenId);

                var administrator = _executionContext.UserId;

                var token = UserToken.Create(
                    tokenId,
                    identity.Id,
                    TokenType.PasswordReset,
                    material.Hash,
                    now,
                    now + policy.PasswordResetTokenLifetime,
                    administrator);

                await _userTokenRepository.AddAsync(token, ct);

                credential.RequirePasswordChange();

                foreach (var priorTokenId in superseded)
                {
                    _auditEvents.Emit("TokenInvalidated", version: 1)
                        .Primary("Token", priorTokenId.Value)
                        .Ref("Identity", identity.Id.Value, role: "Target")
                        .Ref("Token", tokenId.Value, role: "SupersededBy")
                        .WithPayload(new
                        {
                            TokenType = nameof(TokenType.PasswordReset),
                            Reason = "Superseded",
                        });
                }

                // NEVER the token or its hash.
                _auditEvents.Emit("TokenIssued", version: 1)
                    .Primary("Token", tokenId.Value)
                    .Ref("Identity", identity.Id.Value, role: "Target")
                    .Ref("User", subject.Id.Value, role: "Subject")
                    .WithPayload(new
                    {
                        tokenType = nameof(TokenType.PasswordReset),
                        expiresAt = token.ExpiresAt,
                    });

                // Credential-primary: what changed is the credential's state.
                // The administrator's reason travels with this record.
                _auditEvents.Emit("AdminPasswordResetIssued", version: 1)
                    .Primary("Credential", credential.Id.Value)
                    .Ref("Identity", identity.Id.Value, role: "Target")
                    .Ref("User", subject.Id.Value, role: "Subject")
                    .Ref("Token", tokenId.Value, role: "Issued")
                    .WithPayload(new
                    {
                        mustChangePassword = credential.MustChangePassword,
                    })
                    .WithReason(command.Reason);

                // NOT-P1. AdminPasswordReset rather than PasswordReset: the
                // message says an administrator asked, which is what happened.
                _notificationEvents.Emit(
                    NotificationType.AdminPasswordReset,
                    token,
                    subject.Email.Value,
                    material.PlainText);

                return AdminResetPasswordResult.Accepted;
            },
            cancellationToken);
    }
}
