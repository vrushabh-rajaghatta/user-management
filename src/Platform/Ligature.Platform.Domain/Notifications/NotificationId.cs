using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Notifications;

public sealed class NotificationId : StronglyTypedId
{
    public NotificationId(Guid value)
        : base(value)
    {
    }

    public static NotificationId New()
        => new(Guid.NewGuid());
}
