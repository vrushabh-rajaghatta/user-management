using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class SecurityPolicyId : StronglyTypedId
{
    public SecurityPolicyId(Guid value)
        : base(value)
    {
    }

    public static SecurityPolicyId New()
        => new(Guid.NewGuid());
}