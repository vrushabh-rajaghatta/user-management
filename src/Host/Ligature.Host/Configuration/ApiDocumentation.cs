namespace Ligature.Host.Configuration;

/// <summary>
/// Whether the host publishes its OpenAPI document and the Scalar reference UI
/// (docs/architecture.md section 18).
///
/// This is deliberately NOT <c>IHostEnvironment.IsDevelopment()</c>, which is
/// what Microsoft's own sample and the .NET templates use. The environment name
/// is ambient, inherited and settable from outside the deployment, and
/// Program.cs already refuses to register a developer exception page for
/// exactly that reason. The API surface of an authentication host is worth the
/// same care: publishing it must be something an operator did on purpose, not
/// something an inherited ASPNETCORE_ENVIRONMENT can turn on by accident.
/// </summary>
public static class ApiDocumentation
{
    /// <summary>
    /// Absent or empty means OFF. That is the important half of this method:
    /// the safe state is the one you get by doing nothing, so forgetting the
    /// setting in production cannot publish the surface.
    ///
    /// A value that is present but not a boolean THROWS rather than falling
    /// back to off. A typo would otherwise be indistinguishable from a
    /// deliberate refusal, and an operator who wrote "yes" expecting docs would
    /// get silence instead of an answer. The host already refuses to start on a
    /// missing connection string and an undersized signing key; this is the
    /// same rule applied to the same class of mistake.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[HostConfiguration.ApiDocumentationSetting];

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!bool.TryParse(value.Trim(), out var enabled))
        {
            throw new InvalidOperationException(
                $"{HostConfiguration.ApiDocumentationSetting} is set to "
                + $"'{value}', which is not 'true' or 'false'. Remove the "
                + "setting to leave API documentation disabled, or set it to "
                + "'true' to publish the OpenAPI document and the Scalar "
                + "reference UI.");
        }

        return enabled;
    }
}
