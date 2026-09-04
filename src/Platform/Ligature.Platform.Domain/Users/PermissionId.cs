using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class PermissionId : StronglyTypedId
{
    public PermissionId(Guid value)
        : base(value)
    {
    }

    public static PermissionId New()
        => new(Guid.NewGuid());
}