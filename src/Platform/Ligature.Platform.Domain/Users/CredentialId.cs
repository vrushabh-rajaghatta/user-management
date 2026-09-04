using Ligature.SharedKernel.Primitives;

namespace Ligature.Platform.Domain.Users;

public sealed class CredentialId : StronglyTypedId
{
    public CredentialId(Guid value)
        : base(value)
    {
    }

    public static CredentialId New()
        => new(Guid.NewGuid());
}