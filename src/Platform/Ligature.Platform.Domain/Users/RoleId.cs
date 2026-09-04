using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class RoleId : StronglyTypedId
{
    public RoleId(Guid value)
        : base(value)
    {
    }

    public static RoleId New()
        => new(Guid.NewGuid());
}