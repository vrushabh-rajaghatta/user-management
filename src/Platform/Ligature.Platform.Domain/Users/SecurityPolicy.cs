using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class SecurityPolicy : Entity<SecurityPolicyId>
{
    // EF Core materialization only.
    private SecurityPolicy()
    {
    }

    private SecurityPolicy(
        SecurityPolicyId id,
        int policyVersion,
        DateTimeOffset effectiveFrom,
        SecurityPolicySettings settings,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        PolicyVersion = policyVersion;
        EffectiveFrom = effectiveFrom;
        Settings = settings;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public int PolicyVersion { get; }

    public DateTimeOffset EffectiveFrom { get; }

    public SecurityPolicySettings Settings { get; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public static SecurityPolicy Create(
        SecurityPolicyId id,
        int policyVersion,
        DateTimeOffset effectiveFrom,
        SecurityPolicySettings settings,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (policyVersion <= 0)
            throw new DomainException(
                "Policy version must be greater than zero.");

        ArgumentNullException.ThrowIfNull(settings);

        return new SecurityPolicy(
            id,
            policyVersion,
            effectiveFrom,
            settings,
            createdAt,
            createdBy);
    }
}