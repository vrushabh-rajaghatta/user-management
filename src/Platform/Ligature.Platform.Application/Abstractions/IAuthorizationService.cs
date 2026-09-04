namespace Ligature.Platform.Application.Abstractions;

public interface IAuthorizationService
{
    Task<bool> IsAllowedAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken);
}