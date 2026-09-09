namespace Ligature.Platform.Application.Abstractions;

public interface IAuthorizationService
{
    Task<AuthorizationResult> IsAllowedAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken);
}
