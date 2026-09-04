using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class UserIdentityId : StronglyTypedId
{
    public UserIdentityId(Guid value)
        : base(value)
    {
    }

    public static UserIdentityId New()
        => new(Guid.NewGuid());
}