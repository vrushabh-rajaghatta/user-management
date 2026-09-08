using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Persistence;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Provisioning;

/// <summary>
/// The provisioning entry point (PRV-C1 and PRV-C3).
///
/// A separate process rather than a verb on the Host, for two reasons. The Host
/// deliberately does not provision (docs/architecture.md §4), and PE2's
/// eventual privilege model puts seeding under a migration role while the
/// application runs under one that can only read — which needs its own
/// connection, and therefore its own process.
///
/// SEEDS ONLY. Schema stays with `dotnet ef database update`, so the two remain
/// separately auditable steps, and this refuses to run against a database whose
/// migrations are not current rather than failing later with a missing table.
/// </summary>
public static class Program
{
    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int ProvisioningFailed = 2;

    public static async Task<int> Main(string[] args)
        => await RunAsync(args, Console.Out, Console.Error, CancellationToken.None);

    /// <summary>
    /// Writers are injected so the tests can read what an operator would see —
    /// including, and especially, verifying what is NOT in it.
    /// </summary>
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (ProvisioningOptions.IsHelpRequest(args))
        {
            output.WriteLine(ProvisioningOptions.Usage);

            return args.Length == 0 ? UsageError : Success;
        }

        var options = ProvisioningOptions.Parse(args, out var parseError);

        if (options is null)
        {
            error.WriteLine(parseError);
            error.WriteLine();
            error.WriteLine(ProvisioningOptions.Usage);

            return UsageError;
        }

        var connectionString = Environment.GetEnvironmentVariable(
            ProvisioningOptions.ConnectionSetting);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine(
                $"{ProvisioningOptions.ConnectionSetting} is not set. This tool "
                + "writes the platform baseline and the first administrator, so "
                + "it will not guess which database to write them to.");

            return UsageError;
        }

        try
        {
            return await ProvisionAsync(
                options, connectionString, output, error, cancellationToken);
        }
        catch (ProvisioningException failure)
        {
            // The provisioners' own vocabulary. Their messages are written for
            // whoever is holding the terminal, so they are surfaced verbatim.
            error.WriteLine(failure.Message);

            return ProvisioningFailed;
        }
    }

    private static async Task<int> ProvisionAsync(
        ProvisioningOptions options,
        string connectionString,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        await using var provider = BuildProvider(connectionString);

        await using var scope = provider.CreateAsyncScope();

        var services = scope.ServiceProvider;

        var context = services.GetRequiredService<LigatureDbContext>();

        var pending = await context.Database
            .GetPendingMigrationsAsync(cancellationToken);

        if (pending.Any())
        {
            // Distinguishes "the schema is not current" from "provisioning
            // failed", which a missing-table error from deep inside a seed
            // would not.
            error.WriteLine(
                $"The database has {pending.Count()} pending migration(s), so "
                + "its schema is not current. This tool seeds data only. Run:"
                + Environment.NewLine
                + Environment.NewLine
                + "  dotnet ef database update --project "
                + "src/Platform/Ligature.Platform.Persistence"
                + Environment.NewLine);

            return ProvisioningFailed;
        }

        var now = services.GetRequiredService<IClock>().UtcNow;

        await services.GetRequiredService<PlatformProvisioner>()
            .ProvisionAsync(now, cancellationToken);

        var administrator = await services
            .GetRequiredService<BootstrapAdministratorProvisioner>()
            .ProvisionAsync(
                new BootstrapAdministratorRequest(
                    options.FirstName,
                    options.LastName,
                    options.DisplayName,
                    options.Email,
                    options.Username),
                now,

                // Delivery. Invoked only when an administrator is actually
                // being created, so the idempotent path below never creates,
                // touches or regenerates the token file.
                (result, ct) => ActivationTokenFile.WriteAsync(
                    options.ActivationTokenOut, result.ActivationToken, ct),
                cancellationToken);

        if (administrator is null)
        {
            // Idempotent, and therefore a success. Both provisioners are
            // sentinel-guarded; running twice is the design, not a mistake.
            output.WriteLine(
                "Provisioning was already complete. Nothing was changed, and "
                + "no activation token was written.");

            return Success;
        }

        // THE ACTIVATION TOKEN IS NEVER PRINTED. It is a live credential; the
        // path is not.
        output.WriteLine("Provisioning completed.");
        output.WriteLine($"Bootstrap administrator: {options.Username}");
        output.WriteLine(
            "Activation token written to: "
            + Path.GetFullPath(options.ActivationTokenOut));
        output.WriteLine(
            $"The token expires at {administrator.ActivationTokenExpiresAt:u}. "
            + "Deliver it to the administrator and delete the file.");

        return Success;
    }

    /// <summary>
    /// The provisioners are registered HERE rather than in
    /// AddPlatformPersistence, so the Host's container never gains provisioning
    /// types it is not permitted to use.
    /// </summary>
    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection()
            .AddPlatformPersistence(connectionString);

        services.AddScoped<PlatformProvisioner>();
        services.AddScoped<BootstrapAdministratorProvisioner>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
