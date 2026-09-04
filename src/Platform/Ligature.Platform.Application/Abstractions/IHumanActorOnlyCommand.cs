using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Abstractions;

public interface IHumanActorOnlyCommand<TResult>
    : ICommand<TResult>
{
}