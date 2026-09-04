using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class UserRoleId : StronglyTypedId
{
    public UserRoleId(Guid value)
        : base(value)
    {
    }

    public static UserRoleId New()
        => new(Guid.NewGuid());
}