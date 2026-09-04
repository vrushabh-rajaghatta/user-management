using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class RolePermissionId : StronglyTypedId
{
    public RolePermissionId(Guid value)
        : base(value)
    {
    }

    public static RolePermissionId New()
        => new(Guid.NewGuid());
}