using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Abstractions;

public interface IAuthorizableCommand<TResult>
    : ICommand<TResult>
{
    string RequiredPermission { get; }
}