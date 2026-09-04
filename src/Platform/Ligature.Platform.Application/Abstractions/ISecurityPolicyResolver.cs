using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface ISecurityPolicyResolver
{
    Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken);
}