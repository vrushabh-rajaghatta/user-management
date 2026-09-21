using SKSMCorp.SharedKernel.Primitives;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class CredentialId : StronglyTypedId
{
    public CredentialId(Guid value)
        : base(value)
    {
    }

    public static CredentialId New()
        => new(Guid.NewGuid());
}