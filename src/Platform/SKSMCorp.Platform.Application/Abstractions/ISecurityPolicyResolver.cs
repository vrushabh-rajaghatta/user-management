using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

public interface ISecurityPolicyResolver
{
    Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken);
}