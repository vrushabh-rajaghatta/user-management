using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Abstractions;

public interface IAuthorizableCommand<TResult>
    : ICommand<TResult>
{
    string RequiredPermission { get; }
}