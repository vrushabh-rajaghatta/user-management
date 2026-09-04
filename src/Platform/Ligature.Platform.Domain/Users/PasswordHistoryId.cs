using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class PasswordHistoryId : StronglyTypedId
{
    public PasswordHistoryId(Guid value)
        : base(value)
    {
    }

    public static PasswordHistoryId New()
        => new(Guid.NewGuid());
}