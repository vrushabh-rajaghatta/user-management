using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Commands.ReissueActivationLink;

/// <summary>
/// CRD-C7 — ReissueActivationLink. RED STUB: the contract is in
/// docs/requirements.md, and this type exists only so the tests that describe
/// it compile.
/// </summary>
public sealed record ReissueActivationLinkCommand(
    UserId UserId,
    string Reason)
    : IAuthorizableCommand<ReissueActivationLinkResult>
{
    public string RequiredPermission
        => throw new NotImplementedException("CRD-C7 is not implemented.");
}
