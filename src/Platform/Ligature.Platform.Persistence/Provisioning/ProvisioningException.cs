namespace Ligature.Platform.Persistence.Provisioning;

public sealed class ProvisioningException : Exception
{
    public ProvisioningException(string message)
        : base(message)
    {
    }
}
