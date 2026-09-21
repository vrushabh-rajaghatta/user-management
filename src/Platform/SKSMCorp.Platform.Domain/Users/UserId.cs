using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserId : StronglyTypedId
{
    public UserId(Guid value) : base(value)
    {
    }

    public static UserId New() => new (Guid.NewGuid());
}