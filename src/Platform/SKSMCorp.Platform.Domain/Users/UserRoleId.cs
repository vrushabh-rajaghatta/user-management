using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserRoleId : StronglyTypedId
{
    public UserRoleId(Guid value)
        : base(value)
    {
    }

    public static UserRoleId New()
        => new(Guid.NewGuid());
}