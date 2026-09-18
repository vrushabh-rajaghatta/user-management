using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.ReissueActivationLink;

/// <summary>
/// CRD-C7 — ReissueActivationLink.
///
/// The recovery for a failed or expired activation mail: USR-C1 cannot be
/// re-run for the same address or username, and CRD-C5 refuses a user with no
/// credential. This ISSUES A TOKEN AND NOTHING ELSE — no user, identity or
/// credential, no status change, no access. The user still activates through
/// CRD-C1, with the new link.
///
/// ELIGIBILITY IS THE SERVER'S ALONE. A client may one day know which users
/// are pending; that is a presentation optimisation, and every rule below is
/// re-verified whatever the client believed.
///
/// EVERY CHECK BEFORE ANY WRITE, inside the pipeline's transaction: a refused
/// target leaves no token, no invalidation, no notification and no audit
/// record. Everything after the checks commits together or not at all, so a
/// failure there leaves the prior link open and usable.
///
/// A notification already queued for a superseded token is not touched here.
/// The Notification gate (N15) reads the token immediately before transport,
/// finds it invalidated, and closes that row TokenNotLive.
///
/// No concurrency contract beyond the invariant: UT4's index means two
/// concurrent reissues can never leave two open links, and what the loser
/// observes is deliberately unspecified.
/// </summary>
public sealed class ReissueActivationLinkCommandHandler
    : ICommandHandler<ReissueActivationLinkCommand, ReissueActivationLinkResult>
{
    /// <summary>
    /// One message for every ineligible target, whichever rule failed. The
    /// wording is not a frozen contract; its sameness is.
    /// </summary>
    private const string NotEligible =
        "An activation link cannot be sent to this user.";

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

    public ReissueActivationLinkCommandHandler(
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

    public async Task<ReissueActivationLinkResult> Handle(
        ReissueActivationLinkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Refused before any database work. TokenIssued does not require a
        // reason, so nothing downstream would catch a missing one: this check
        // is the only thing that makes it mandatory.
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessRuleViolationException(
                "A reason is required to send an activation link.");
        }

        var now = _clock.UtcNow;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // ---- Eligibility. Reads only; nothing below may be skipped.

                var subject = await _userRepository.FindAsync(command.UserId, ct);

                // Human only, and an email because it is the only delivery
                // channel. The System actor fails the actor-type check.
                if (subject is null
                    || subject.ActorType != ActorType.Human
                    || subject.Status != UserStatus.Active
                    || subject.Email is null)
                {
                    throw new BusinessRuleViolationException(NotEligible);
                }

                // Exactly one active local identity, for CRD-C5's reason:
                // choosing between two would be a guess about whose mailbox
                // controls the account.
                var locals = await _userIdentityRepository
                    .FindLocalByUserIdAsync(subject.Id, ct);

                if (locals.Count != 1 || locals[0].Status != UserStatus.Active)
                    throw new BusinessRuleViolationException(NotEligible);

                var identity = locals[0];

                // No credential IS the pending-activation state (inv. 15). A
                // user who has activated is CRD-C5's; serving them here would
                // make this a second password-reset path.
                var credential = await _credentialRepository
                    .FindByIdentityAsync(identity.Id, ct);

                if (credential is not null)
                    throw new BusinessRuleViolationException(NotEligible);

                // ---- Writes and declarations.

                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                // UT5 before the insert: an expired but unused token still
                // holds UT4's slot. After this, the new token is the identity's
                // only open activation link.
                var superseded = await _userTokenRepository.InvalidatePriorAsync(
                    identity.Id, TokenType.Activation, now, ct);

                var tokenId = UserTokenId.New();

                // Only the hash is persisted; the plaintext's one consumer is
                // the notification below (UT7). It is not in the result.
                var material = _userTokenService.Generate(tokenId);

                var token = UserToken.Create(
                    tokenId,
                    identity.Id,
                    TokenType.Activation,
                    material.Hash,
                    now,
                    now + policy.ActivationTokenLifetime,
                    _executionContext.UserId);

                await _userTokenRepository.AddAsync(token, ct);

                foreach (var priorTokenId in superseded)
                {
                    _auditEvents.Emit("TokenInvalidated", version: 1)
                        .Primary("Token", priorTokenId.Value)
                        .Ref("Identity", identity.Id.Value, role: "Target")
                        .Ref("Token", tokenId.Value, role: "SupersededBy")
                        .WithPayload(new
                        {
                            TokenType = nameof(TokenType.Activation),
                            Reason = "Superseded",
                        });
                }

                // NEVER the token or its hash. The administrator's reason
                // travels here: there is no reissue event, and this is the
                // record of the administrator's act.
                _auditEvents.Emit("TokenIssued", version: 1)
                    .Primary("Token", tokenId.Value)
                    .Ref("Identity", identity.Id.Value, role: "Target")
                    .Ref("User", subject.Id.Value, role: "Subject")
                    .WithPayload(new
                    {
                        tokenType = nameof(TokenType.Activation),
                        expiresAt = token.ExpiresAt,
                    })
                    .WithReason(command.Reason);

                // NOT-P1. The same message USR-C1 sends.
                _notificationEvents.Emit(
                    NotificationType.AccountActivation,
                    token,
                    subject.Email.Value,
                    material.PlainText);

                return ReissueActivationLinkResult.Accepted;
            },
            cancellationToken);
    }
}
