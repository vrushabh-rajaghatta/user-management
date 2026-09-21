using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserIdentityId : StronglyTypedId
{
    public UserIdentityId(Guid value)
        : base(value)
    {
    }

    public static UserIdentityId New()
        => new(Guid.NewGuid());
}