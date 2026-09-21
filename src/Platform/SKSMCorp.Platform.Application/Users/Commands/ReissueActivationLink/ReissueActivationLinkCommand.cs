using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Commands.ReissueActivationLink;

/// <summary>
/// CRD-C7. An administrator sends a user who has never activated a new
/// activation link, replacing every earlier one.
///
/// user.create, not a permission of its own (owner decision, recorded in
/// docs/requirements.md): reissuing the activation token finishes the work
/// creation started, reaches no one a creator could not, and grants nothing.
/// </summary>
/// <param name="Reason">
/// Required. It is recorded on TokenIssued, which accepts but does not require
/// one, so the handler — not the audit catalogue — is what makes it mandatory.
/// A human explanation from the administrator, never a code (AUD-7).
/// </param>
public sealed record ReissueActivationLinkCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<ReissueActivationLinkResult>,
      IHumanActorOnlyCommand<ReissueActivationLinkResult>
{
    public string RequiredPermission => "user.create";
}
