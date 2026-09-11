namespace Ligature.Host.Configuration;

/// <summary>
/// The setting names the host reads. Named constants rather than string
/// literals scattered across Program.cs and the tests, so a rename cannot leave
/// the two disagreeing about what the environment is called.
/// </summary>
public static class HostConfiguration
{
    /// <summary>
    /// The same variable the persistence tests and the EF design-time factory
    /// read, so one environment serves all three.
    /// </summary>
    public const string ConnectionSetting = "LIGATURE_CONNECTION";

    /// <summary>
    /// Opts the OpenAPI document and the Scalar reference UI in
    /// (docs/architecture.md section 18). Absent means off.
    /// </summary>
    public const string ApiDocumentationSetting = "LIGATURE_API_DOCUMENTATION";

    /// <summary>
    /// The externally reachable origin the activation link is built from — the
    /// FRONTEND's origin, not the API's. The emailed link is a browser GET and
    /// the activation endpoint needs a token and a password together, so the
    /// link lands on a page which then calls the API.
    /// </summary>
    public const string PublicBaseUrlSetting = "LIGATURE_PUBLIC_BASE_URL";

    /// <summary>
    /// The five mail settings. All absent means delivery is disabled and the
    /// host still starts; any subset present means a misconfiguration and the
    /// host refuses. Only the key is secret.
    /// </summary>
    public const string MailSenderAddressSetting = "LIGATURE_MAIL_SENDER_ADDRESS";

    public const string MailSenderNameSetting = "LIGATURE_MAIL_SENDER_NAME";

    public const string MailServiceAccountSetting = "LIGATURE_MAIL_SERVICE_ACCOUNT";

    /// <summary>
    /// The service account's private key, base64 of the PEM. Base64 because a
    /// PEM is multi-line and environment variables are not, and because the
    /// signing key ring already establishes base64-in-one-variable as how this
    /// repository carries key material.
    /// </summary>
    public const string MailServiceAccountKeySetting = "LIGATURE_MAIL_SERVICE_ACCOUNT_KEY";
}
