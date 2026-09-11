using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.RequestPasswordReset;

/// <summary>
/// CRD-C2 — RequestPasswordReset.
///
/// TWO BRANCHES, ONE ANSWER. Where exactly one eligible account is found this
/// supersedes that identity's prior reset tokens, issues a new one, and
/// declares the mail. Where none is found, or more than one, it does NOTHING
/// AT ALL — no token, no notification row, no audit record — and returns the
/// same result. The uniformity is the security property: an endpoint whose
/// answer varies with account existence is an enumeration oracle, and that is
/// this command's frozen failure mode.
///
/// The silent branch writes no audit event either, and that is deliberate
/// rather than an omission. PasswordResetRequested requires a Token primary
/// entity and Identity and User refs (catalogue), so it is unconstructible
/// without an issued token; D-NOTIF-03 records the same conclusion for the
/// notification row. A request naming an unknown account therefore leaves no
/// trace anywhere.
///
/// BLOCKING DEPENDENCY — RATE LIMITING IS NOT IMPLEMENTED. The command
/// catalogue makes "rate limited per address and per IP" a PRECONDITION of
/// this command, and Notification's D-NOTIF-03 names pipeline behaviour 11 as
/// the compensating control for the residual timing difference between these
/// two branches — a difference that design knowingly accepts BECAUSE rate
/// limiting compensates for it. Behaviour 11 does not exist. This command is
/// therefore implemented but NOT first-tenant-ready, and the silent branch
/// above is currently the only thing standing between an attacker and an
/// enumeration attempt they can make as often as they like. Recorded in
/// docs/requirements.md; it must land before any tenant sees this.
///
/// Account lockout (FailedAttemptCount/LockedUntil) does not help here: it
/// guards password attempts against one credential, not repeated reset
/// requests across many addresses.
/// </summary>
public sealed class RequestPasswordResetCommandHandler
    : ICommandHandler<RequestPasswordResetCommand, RequestPasswordResetResult>
{
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenService _userTokenService;
    private readonly IAuditEvents _auditEvents;
    private readonly INotificationEvents _notificationEvents;

    public RequestPasswordResetCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserTokenRepository userTokenRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService,
        IAuditEvents auditEvents,
        INotificationEvents notificationEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(userTokenRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(userTokenService);
        ArgumentNullException.ThrowIfNull(auditEvents);
        ArgumentNullException.ThrowIfNull(notificationEvents);

        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _userTokenRepository = userTokenRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
        _auditEvents = auditEvents;
        _notificationEvents = notificationEvents;
    }

    public async Task<RequestPasswordResetResult> Handle(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;

        // An empty or blank input cannot match anything, and takes the silent
        // branch rather than throwing: a validation error here would be a
        // different response, which is the one thing this command must never
        // produce. It also never reaches the database.
        if (string.IsNullOrWhiteSpace(command.EmailOrUsername))
            return RequestPasswordResetResult.Accepted;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // D2 — resolve by username AND email in one statement, then
                // require EXACTLY ONE eligible identity.
                //
                // Not "the first match", and not username-first: a username is
                // an unconstrained label, so one input can match one person's
                // username and a different person's email. Any precedence rule
                // would be arbitrary and would silently favour one of the two.
                // Requiring exactly one fails closed — a collision means
                // neither self-serves, and an administrator can still use
                // CRD-C5.
                var candidates = await _userIdentityRepository
                    .FindPasswordResetCandidatesAsync(command.EmailOrUsername, ct);

                if (candidates.Count != 1)
                    return RequestPasswordResetResult.Accepted;

                var identityId = candidates[0];

                var identity = await _userIdentityRepository.FindAsync(identityId, ct);

                if (identity is null)
                    return RequestPasswordResetResult.Accepted;

                var subject = await _userRepository.FindAsync(identity.UserId, ct);

                // The resolution already required a non-null email; this is
                // the compiler's proof, not a second rule. Both absences take
                // the silent branch, because there is nowhere to send a link.
                if (subject?.Email is null)
                    return RequestPasswordResetResult.Accepted;

                // PasswordResetTokenLifetime is a CAP — min(tenant, baseline)
                // — resolved now, so a tightened baseline applies immediately
                // (inv. 30, 31).
                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                // UT5 — supersede every prior unused reset token for this
                // identity, INCLUDING already-expired ones, before issuing.
                //
                // Order matters and is not stylistic. UT4's unique index is
                // "unused AND uninvalidated" and cannot mention expiry, so an
                // expired-but-unused token still holds the slot: invalidating
                // after the insert would collide, and invalidating only the
                // unexpired ones would collide too. Doing it first means only
                // one reset link is ever live, which is what limits exposure
                // if an earlier mail was intercepted.
                var superseded = await _userTokenRepository.InvalidatePriorAsync(
                    identityId, TokenType.PasswordReset, now, ct);

                var tokenId = UserTokenId.New();

                // Only the hash is persisted. The plaintext has exactly one
                // consumer — the notification declared below — and lives in
                // memory for this command and nowhere else (UT7).
                var material = _userTokenService.Generate(tokenId);

                var token = UserToken.Create(
                    tokenId,
                    identityId,
                    TokenType.PasswordReset,
                    material.Hash,
                    now,
                    now + policy.PasswordResetTokenLifetime,
                    // SYSTEM_UUID, not the subject. The person at the keyboard
                    // is not authenticated and may not be the account owner;
                    // recording the owner as issuer would assert an act they
                    // may not have performed. It also makes self-service and
                    // administrator-initiated resets distinguishable in
                    // forensics, which UM §6.5 calls for directly.
                    User.SystemUserId);

                await _userTokenRepository.AddAsync(token, ct);

                // One event per superseded token, not one for the batch: each
                // token is its own record with its own provenance, and a
                // reviewer asking "what happened to the link I was sent"
                // needs the row for THAT token.
                //
                // AsSystem for the same reason the token's CreatedBy is: no
                // caller authorised this. The catalogue permits both
                // Authenticated and System origins for TokenInvalidated,
                // because USR-C1 emits the same event as an administrator.
                foreach (var priorTokenId in superseded)
                {
                    _auditEvents.Emit("TokenInvalidated", version: 1)
                        .AsSystem()
                        .Primary("Token", priorTokenId.Value)
                        .Ref("Identity", identityId.Value, role: "Target")
                        .Ref("Token", tokenId.Value, role: "SupersededBy")
                        .WithPayload(new
                        {
                            TokenType = nameof(TokenType.PasswordReset),
                            Reason = "Superseded",
                        });
                }

                // AsSystem is required, not chosen: the catalogue permits only
                // a System origin for this event, and an anonymous scope would
                // otherwise produce Anonymous and be refused by behaviour 14.
                //
                // NEVER the token or its hash. RequestIp is PascalCase to
                // match the catalogue's declared PII path Payload.RequestIp
                // exactly — the secret scan judges names everywhere, and a
                // declared path that does not match is not recognised as PII.
                _auditEvents.Emit("PasswordResetRequested", version: 1)
                    .AsSystem()
                    .Primary("Token", tokenId.Value)
                    .Ref("Identity", identityId.Value, role: "Target")
                    .Ref("User", subject.Id.Value, role: "Subject")
                    .WithPayload(new
                    {
                        TokenType = nameof(TokenType.PasswordReset),
                        ExpiresAt = token.ExpiresAt,
                        RequestIp = command.IpAddress,
                    });

                // NOT-P1, on the known-account branch ONLY. The pipeline
                // writes the Pending row after this handler returns and still
                // inside this transaction, so the token and the intent to
                // deliver it commit together or not at all.
                //
                // Its placement IS the enumeration control (D-NOTIF-03): the
                // silent branch never reaches this line, so no row exists to
                // betray that an address was known.
                _notificationEvents.Emit(
                    NotificationType.PasswordReset,
                    tokenId,
                    subject.Email.Value,
                    material.PlainText);

                return RequestPasswordResetResult.Accepted;
            },
            cancellationToken);
    }
}
