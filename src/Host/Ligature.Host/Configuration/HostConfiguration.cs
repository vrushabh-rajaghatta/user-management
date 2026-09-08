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
}
