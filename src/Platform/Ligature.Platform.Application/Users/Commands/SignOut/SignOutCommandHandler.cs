using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.SignOut;

/// <summary>
/// SES-C2 — SignOut.
///
/// D6: a session ends through ONE mechanism, revocation. Signing out is a
/// self-revocation — the same three columns an administrator or a cascade
/// writes, with a different actor and reason. There is no LogoutAt column,
/// because a second way to end a session would be a second source of truth.
///
/// EVERY OUTCOME IS THE SAME OUTCOME. An unknown session, a session belonging
/// to someone else, an already-revoked session and a successful revocation are
/// indistinguishable to the caller. Distinguishing them would turn SessionId
/// into an existence oracle: session ids are supplied by the caller, so a
/// command that answered "that one is not yours" differently from "that one
/// does not exist" would let anyone enumerate live sessions.
/// </summary>
public sealed class SignOutCommandHandler
    : ICommandHandler<SignOutCommand, SignOutResult>
{
    /// <summary>
    /// One of the reasons the frozen model enumerates for user_session:
    /// Logout / SignOutEverywhere / UserDeactivated / IdentityDeactivated /
    /// AdminRevoked / IdleTimeout. The reason is what makes a revoked row
    /// readable years later.
    /// </summary>
    private const string LogoutReason = "Logout";

    private readonly IClock _clock;
    private readonly IExecutionContext _executionContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;

    public SignOutCommandHandler(
        IClock clock,
        IExecutionContext executionContext,
        IUnitOfWork unitOfWork,
        IUserSessionRepository userSessionRepository,
        IUserIdentityRepository userIdentityRepository)
    {
        _clock = clock;
        _executionContext = executionContext;
        _unitOfWork = unitOfWork;
        _userSessionRepository = userSessionRepository;
        _userIdentityRepository = userIdentityRepository;
    }

    public async Task<SignOutResult> Handle(
        SignOutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;
        var caller = _executionContext.UserId;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var session = await _userSessionRepository
                    .FindAsync(command.SessionId, ct);

                if (session is null)
                    return new SignOutResult();

                // Ownership is checked here rather than granted by the
                // pipeline, because "self" is a relationship and not a
                // catalogue permission. Without this check SES-C2 would BE
                // SES-C3 RevokeSession — which requires session.revoke —
                // available to every authenticated user.
                var identity = await _userIdentityRepository
                    .FindAsync(session.UserIdentityId, ct);

                if (identity is null || identity.UserId != caller)
                    return new SignOutResult();

                // Returns false when the session was already revoked. Ignored
                // deliberately: a double sign-out is idempotent, not an error,
                // and the first revocation's actor, reason and instant are
                // write-once and must survive.
                //
                // An expired session is still revocable — expiry is derived
                // from timestamps rather than stored, so there is no state to
                // conflict with.
                _ = session.Revoke(now, caller, LogoutReason);

                // TODO — SES-C2. Declare SignedOut through IAuditEvents, and
                // register the command in AuditDeclarations; the pipeline
                // writes it inside this transaction (docs/architecture.md
                // section 11). Note that the audit event
                // is where an attempt against someone else's session becomes
                // visible: this command deliberately tells the caller nothing.

                return new SignOutResult();
            },
            cancellationToken);
    }
}
