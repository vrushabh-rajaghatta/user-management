using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Abstractions;

public interface IHumanActorOnlyCommand<TResult>
    : ICommand<TResult>
{
}