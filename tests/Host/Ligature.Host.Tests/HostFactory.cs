using Ligature.Host.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ligature.Host.Tests;

/// <summary>
/// Runs the real host — real middleware, real routing, real model binding, real
/// command pipeline, real PostgreSQL. Only the configuration is supplied from
/// here, and it is supplied the same way a deployment would supply it.
///
/// Nothing is stubbed. The point of this suite is the seam between HTTP and the
/// platform, and a factory that replaced the persistence registrations would
/// prove the seam works against something that is not the system.
/// </summary>
internal sealed class HostFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Thirty-two bytes exactly — the section 17 minimum — so the tests run
    /// against the smallest key the host will accept rather than a comfortable
    /// one. A test key in a repository is not a secret: the host has no default
    /// key precisely so that this value can never become one.
    /// </summary>
    internal const string PrimaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDE=";

    /// <summary>
    /// A second configured key, used to prove that a carrier signed by one key
    /// is not accepted under another identifier.
    /// </summary>
    internal const string SecondaryKey =
        "TGlnYXR1cmUgSG9zdCB0ZXN0IHNpZ25pbmcga2V5IDI=";

    internal const string PrimaryKeyId = "v1";

    internal const string SecondaryKeyId = "v2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            HostConfiguration.ConnectionSetting, TestDatabase.ConnectionString);

        builder.UseSetting(SigningKeyRing.CurrentKeySetting, PrimaryKeyId);

        builder.UseSetting(
            SigningKeyRing.KeyPrefix + "V1", PrimaryKey);

        builder.UseSetting(
            SigningKeyRing.KeyPrefix + "V2", SecondaryKey);
    }
}
