using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Commands.ReissueActivationLink;

/// <summary>CRD-C7. RED STUB, and not registered.</summary>
public sealed class ReissueActivationLinkCommandHandler
    : ICommandHandler<ReissueActivationLinkCommand, ReissueActivationLinkResult>
{
    public Task<ReissueActivationLinkResult> Handle(
        ReissueActivationLinkCommand command,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("CRD-C7 is not implemented.");
}
