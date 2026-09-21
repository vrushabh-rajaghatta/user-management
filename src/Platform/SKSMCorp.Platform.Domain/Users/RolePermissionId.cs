using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class RolePermissionId : StronglyTypedId
{
    public RolePermissionId(Guid value)
        : base(value)
    {
    }

    public static RolePermissionId New()
        => new(Guid.NewGuid());
}