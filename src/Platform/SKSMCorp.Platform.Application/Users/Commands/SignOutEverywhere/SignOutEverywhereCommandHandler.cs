using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Users.Commands.SignOutEverywhere;

/// <summary>
/// SES-C4, self form — SignOutEverywhere.
///
/// Every active session of every identity the caller holds (D5), INCLUDING the
/// one making the request unless the caller explicitly keeps it (D4). Each is
/// revoked with the code SignOutEverywhere and recorded as its own
/// SessionRevoked; SignedOut is SES-C2's event and is never emitted here.
///
/// The explanation is optional (R1a): the account holder should not have to
/// justify a routine security action, and the audit Reason — required on every
/// SessionRevoked — then carries a fixed human sentence rather than a code.
///
/// Scoped to the caller's own user by construction, so a current-session id
/// that is not theirs is simply never among the sessions found.
/// </summary>
public sealed class SignOutEverywhereCommandHandler
    : ICommandHandler<SignOutEverywhereCommand, SignOutEverywhereResult>
{
    /// <summary>The audit Reason when the account holder supplies none.</summary>
    internal const string DefaultExplanation = "Signed out of all sessions by the account holder";

    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IAuditEvents _auditEvents;

    public SignOutEverywhereCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserSessionRepository userSessionRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userSessionRepository);
        ArgumentNullException.ThrowIfNull(securityPolicyResolver);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userSessionRepository = userSessionRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _auditEvents = auditEvents;
    }

    public async Task<SignOutEverywhereResult> Handle(
        SignOutEverywhereCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var explanation = string.IsNullOrWhiteSpace(command.Reason)
            ? DefaultExplanation
            : command.Reason;

        var now = _clock.UtcNow;
        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var policy = await _securityPolicyResolver.GetEffectiveSettingsAsync(now, ct);

                var sessions = await _userSessionRepository.FindActiveForUserAsync(
                    caller, now, policy.SessionIdleTimeout, ct);

                foreach (var session in sessions)
                {
                    if (command.KeepCurrentSession && session.Id == command.CurrentSessionId)
                        continue;

                    if (session.Revoke(now, caller, SessionRevocations.SignOutEverywhere))
                        SessionRevocations.Declare(_auditEvents, session, caller, explanation);
                }

                return SignOutEverywhereResult.Done;
            },
            cancellationToken);
    }
}
