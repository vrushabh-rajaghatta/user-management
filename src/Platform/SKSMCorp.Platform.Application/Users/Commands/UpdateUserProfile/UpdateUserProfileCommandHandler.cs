using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Users.Commands.UpdateUserProfile;

/// <summary>
/// USR-C2 — UpdateUserProfile, the administrator command (docs/requirements.md,
/// "USR-C2 — Update User Profile, and USR-Q1 GetUser (narrow v1)").
///
/// load → refuse the System actor → domain update → audit only on a change,
/// in the pipeline's transaction, so the profile and its record commit or
/// roll back together. Authorisation (user.update) has already run.
///
/// DELIBERATELY NARROW. First, last and display name only: never email,
/// status, identities, credentials, sessions or roles. No status check — an
/// inactive user's profile may be corrected (G7) — and so no row lock: the D6
/// lock orders commands that depend on lifecycle status, and this does not.
/// Last write wins in v1 (G6).
/// </summary>
public sealed class UpdateUserProfileCommandHandler : ICommandHandler<UpdateUserProfileCommand, UpdateUserProfileResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IAuditEvents _auditEvents;

    public UpdateUserProfileCommandHandler(IUnitOfWork unitOfWork, IUserRepository userRepository, IAuditEvents auditEvents)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(auditEvents);

        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _auditEvents = auditEvents;
    }

    public async Task<UpdateUserProfileResult> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var user = await _userRepository.FindAsync(command.UserId, ct)
                    ?? throw new BusinessRuleViolationException("The user does not exist.");

                // G8, explicitly and first: the System actor's profile is not
                // this command's to change. AU7 freezes the row in PostgreSQL
                // too; that guard is defence in depth, not the rule.
                if (user.ActorType == ActorType.System)
                    throw new BusinessRuleViolationException("This user's profile cannot be changed.");

                var before = Profile(user);

                bool changed;

                try
                {
                    changed = user.UpdateProfile(command.FirstName, command.LastName, command.DisplayName);
                }
                catch (DomainException refusal)
                {
                    // The name rules are the domain's; their wording reaches the
                    // caller through the application's refusal.
                    throw new BusinessRuleViolationException(refusal.Message);
                }

                // G5 — no change (after normalisation) writes and records
                // nothing. The tracker has nothing to save either.
                if (!changed)
                    return UpdateUserProfileResult.Accepted;

                _auditEvents.Emit("UserProfileChanged", version: 1)
                    .Primary("User", user.Id.Value)
                    .WithBefore(before)
                    .WithAfter(Profile(user));

                return UpdateUserProfileResult.Accepted;
            },
            cancellationToken);
    }

    /// <summary>Exactly the catalogue's Before/After paths, all personal data of the primary subject.</summary>
    private static object Profile(User user)
        => new
        {
            user.FirstName,
            user.LastName,
            user.DisplayName,
        };
}
