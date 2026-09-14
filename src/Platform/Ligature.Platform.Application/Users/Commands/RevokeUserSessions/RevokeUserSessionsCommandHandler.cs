using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.RevokeUserSessions;

/// <summary>
/// SES-C4, administrator form — RevokeUserSessions.
///
/// USER-WIDE (D5). Every active session of every identity the user holds —
/// invariant 29, "all of a user's active sessions". Not CRD-C4's scope, which
/// was one identity; the same active-session test, a wider set.
///
/// NO EXCLUSION (D4). An administrator who targets their own user ends their
/// own current session too: the administrator form has no way to keep a
/// session, because it is not the account holder's to keep.
///
/// An unknown user is refused; a known user with nothing active is a no-op
/// (D7).
/// </summary>
public sealed class RevokeUserSessionsCommandHandler
    : ICommandHandler<RevokeUserSessionsCommand, RevokeUserSessionsResult>
{
    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IAuditEvents _auditEvents;

    public RevokeUserSessionsCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _auditEvents = auditEvents;
    }

    public async Task<RevokeUserSessionsResult> Handle(
        RevokeUserSessionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessRuleViolationException(
                "A reason is required to revoke a user's sessions.");
        }

        var now = _clock.UtcNow;
        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                if (await _userRepository.FindAsync(command.UserId, ct) is null)
                    throw new BusinessRuleViolationException("The user does not exist.");

                var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, ct);

                var sessions = await _userSessionRepository.FindActiveForUserAsync(
                    command.UserId, now, policy.SessionIdleTimeout, ct);

                foreach (var session in sessions)
                {
                    if (session.Revoke(now, caller, SessionRevocations.AdminRevoked))
                        SessionRevocations.Declare(_auditEvents, session, command.UserId, command.Reason);
                }

                return RevokeUserSessionsResult.Done;
            },
            cancellationToken);
    }
}
