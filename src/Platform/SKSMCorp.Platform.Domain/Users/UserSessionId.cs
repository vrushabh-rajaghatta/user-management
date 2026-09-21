using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserSessionId : StronglyTypedId
{
    public UserSessionId(Guid value)
        : base(value)
    {
    }

    public static UserSessionId New()
        => new(Guid.NewGuid());
}