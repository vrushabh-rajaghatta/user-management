using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class UserSessionId : StronglyTypedId
{
    public UserSessionId(Guid value)
        : base(value)
    {
    }

    public static UserSessionId New()
        => new(Guid.NewGuid());
}