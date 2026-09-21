using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Commands.ChangeUserEmail;

/// <summary>
/// USR-C3 — ChangeUserEmail, the administrator command (docs/requirements.md,
/// "USR-C3 ChangeUserEmail"). A3 (c), administrator half: immediate, and
/// attributable in the trail.
///
///   parse  →  lock and load  →  refuse  →  no-op?  →  uniqueness
///   →  change  →  invalidate open tokens  →  UserEmailChanged
///
/// In the pipeline's single transaction, under the target's row lock (CE9,
/// the D6 lock), so the address, the tokens and the record commit or roll back
/// together. Authorisation (user.update) has already run.
///
/// TWO THINGS THIS COMMAND DELIBERATELY DOES NOT DO. It has no rule about who
/// the target is (CE2): an administrator's change to their own record is an
/// administrator change like any other; self-service is a separate, verified,
/// future command. And it issues nothing (CE7): links sent to the old address
/// stop working, and CRD-C7 Resend activation is how a new one is sent.
/// </summary>
public sealed class ChangeUserEmailCommandHandler : ICommandHandler<ChangeUserEmailCommand, ChangeUserEmailResult>
{
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly IAuditEvents _auditEvents;

    public ChangeUserEmailCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserTokenRepository userTokenRepository,
        IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(userTokenRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userTokenRepository = userTokenRepository;
        _auditEvents = auditEvents;
    }

    public async Task<ChangeUserEmailResult> Handle(ChangeUserEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // CE4 — USR-C1's own rule: trimmed, structural, no control character.
        var email = EmailAddress.Create(command.Email);

        // CE3 — optional; a blank reason is no reason, anything else is
        // recorded exactly as sent.
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason;

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // CE9 — lock first, before anything about this user is read.
                var user = await _userRepository.FindForUpdateAsync(command.UserId, ct)
                    ?? throw new BusinessRuleViolationException("The user does not exist.");

                // CE8 — humans only; the System actor and agents have no
                // address for this command to change.
                if (user.ActorType != ActorType.Human)
                    throw new BusinessRuleViolationException("This user's email cannot be changed.");

                // Read AFTER the lock, as USR-C4 does: the instant stamped on
                // the tokens belongs to this command's turn, not its wait.
                var now = _clock.UtcNow;

                var before = user.Email?.Value;

                // CE6 — the same address, ignoring case, is no change: nothing
                // written, nothing recorded, no token touched.
                if (user.Email is not null && user.Email.Equals(email))
                    return ChangeUserEmailResult.Accepted;

                // CE5 — the HOLDER's status matters, not the target's. The
                // index remains the guarantee; a race past this check is
                // refused by it, with the same sentence.
                if (await _userRepository.ExistsOtherActiveHumanWithEmailAsync(email, user.Id, ct))
                    throw new BusinessRuleViolationException("A user with this email address already exists.");

                user.ChangeEmail(email);

                // CE7 — every link sent to the old address stops working. State
                // only: the frozen TokenInvalidated needs a superseding token
                // and there is none (D13). Nothing is issued in its place.
                await _userTokenRepository.InvalidateOutstandingForUserAsync(user.Id, now, ct);

                // CE11 — the record of the change, both addresses being the
                // primary subject's personal data.
                _auditEvents.Emit("UserEmailChanged", version: 1)
                    .Primary("User", user.Id.Value)
                    .WithBefore(new { Email = before })
                    .WithAfter(new { Email = email.Value })
                    .WithReason(reason);

                return ChangeUserEmailResult.Accepted;
            },
            cancellationToken);
    }
}
