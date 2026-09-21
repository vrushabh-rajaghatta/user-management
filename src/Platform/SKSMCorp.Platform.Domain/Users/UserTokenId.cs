using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserTokenId : StronglyTypedId
{
    public UserTokenId(Guid value)
        : base(value)
    {
    }

    public static UserTokenId New()
        => new(Guid.NewGuid());
}