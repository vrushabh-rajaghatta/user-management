using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Commands.RevokeSession;

/// <summary>
/// SES-C3 — RevokeSession.
///
/// UNKNOWN IS AN ERROR, ENDED IS A NO-OP (D6). The caller is authorised over
/// sessions, so an id that names no session is refused plainly. A session that
/// exists but fails the canonical active-session test — revoked, expired, idle
/// beyond the timeout and tolerance, or held by an inactive identity or user —
/// is already over: revoking it would record a termination that changed
/// nothing, so nothing is written and nothing is declared.
///
/// "Active" is not restated here. It is the test the per-request session check
/// applies, owned by the repository beside the tolerance it needs.
/// </summary>
public sealed class RevokeSessionCommandHandler
    : ICommandHandler<RevokeSessionCommand, RevokeSessionResult>
{
    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IAuditEvents _auditEvents;

    public RevokeSessionCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserSessionRepository userSessionRepository,
        IUserIdentityRepository userIdentityRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(userIdentityRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userSessionRepository = userSessionRepository;
        _userIdentityRepository = userIdentityRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _auditEvents = auditEvents;
    }

    public async Task<RevokeSessionResult> Handle(
        RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessRuleViolationException(
                "A reason is required to revoke a session.");
        }

        var now = _clock.UtcNow;
        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (await _userSessionRepository.FindAsync(command.SessionId, ct) is null)
                    throw new BusinessRuleViolationException("The session does not exist.");

                var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, ct);

                var session = await _userSessionRepository.FindActiveAsync(
                    command.SessionId, now, policy.SessionIdleTimeout, ct);

                // Already over (D6): nothing to revoke, nothing to record.
                if (session is null)
                    return RevokeSessionResult.Done;

                var identity = await _userIdentityRepository.FindAsync(session.UserIdentityId, ct)
                    ?? throw new InvalidOperationException(
                        "Session revocation defect — an active session has no identity.");

                if (session.Revoke(now, caller, SessionRevocations.AdminRevoked))
                    SessionRevocations.Declare(_auditEvents, session, identity.UserId, command.Reason);

                return RevokeSessionResult.Done;
            },
            cancellationToken);
    }
}
