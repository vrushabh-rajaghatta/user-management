using System.Reflection;
using Ligature.Platform.Persistence.Audit;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Provisioning;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Ligature.CatalogueSync;

/// <summary>
/// PRV-C2's entry point — catalogue synchronisation (docs/requirements.md).
///
/// A separate process, run as migration_role, between the audit schema and the
/// host. It exists because ProvisionAsync returns as soon as the System actor
/// exists, so a permission a release adds never reaches an existing database.
///
/// THIS FILE INTERPRETS A RESULT; IT DOES NOT PRODUCE ONE. Reconciliation lives
/// in CatalogueSynchroniser, in Persistence, beside the seeds it reads. What
/// happens here is argument handling, the two preconditions, turning an outcome
/// into an exit code, and saying to an operator what happened. A decision about
/// what may be reconciled does not belong in a CLI.
/// </summary>
public static class Program
{
    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int Refused = 2;
    internal const int Failed = 3;

    internal const string ConnectionSetting = "LIGATURE_CONNECTION";

    public static async Task<int> Main(string[] args)
        => await RunAsync(args, Console.Out, Console.Error, CancellationToken.None);

    /// <summary>Writers are injected so tests read what an operator would see.</summary>
    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0)
        {
            error.WriteLine(
                "This tool takes no arguments. It reconciles the release's "
                + $"permission, role and grant catalogue with the database "
                + $"{ConnectionSetting} names.");

            return UsageError;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionSetting);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine(
                $"{ConnectionSetting} is not set. This tool changes a release's "
                + "authorization catalogue, so it will not guess which database "
                + "to change.");

            return UsageError;
        }

        try
        {
            return await SynchroniseAsync(connectionString, output, error, cancellationToken);
        }
        catch (CatalogueAuditNotRecordedException failure)
        {
            // NOT a synchronisation failure, and the message must not read like
            // one. The exception states which outcome went unrecorded, because
            // a refused run committed nothing and an operator told otherwise
            // would go looking for damage that does not exist.
            error.WriteLine(failure.Message);
            error.WriteLine();
            error.WriteLine("No compensation was attempted.");
            error.WriteLine();
            error.WriteLine(failure.InnerException?.Message);

            return Failed;
        }
        catch (Exception failure)
        {
            // Reports and returns, rather than escaping Main: an unhandled
            // exception leaves the container up and the caller waiting, which
            // looks like a hang instead of an error.
            error.WriteLine(
                "Catalogue synchronisation failed. No catalogue mutation was "
                + "committed, so this can be re-run once the cause is fixed.");

            error.WriteLine();
            error.WriteLine(failure.ToString());

            return Failed;
        }
    }

    private static async Task<int> SynchroniseAsync(
        string connectionString,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        await using var context = new LigatureDbContext(
            new DbContextOptionsBuilder<LigatureDbContext>()
                .UseNpgsql(connectionString)
                .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
                .Options);

        // Both preconditions run BEFORE any reconciliation. Each distinguishes
        // "a step was skipped" from "synchronisation failed", which a
        // missing-table error from inside the reconciliation would not.
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);

        if (pending.Any())
        {
            error.WriteLine(
                $"The database has {pending.Count()} pending migration(s), so its "
                + "schema is not current. This tool reconciles catalogue data only. Run:"
                + Environment.NewLine + Environment.NewLine
                + "  dotnet ef database update --project src/Platform/Ligature.Platform.Persistence"
                + Environment.NewLine);

            return Failed;
        }

        // The audit schema carries 005, which grants this tool's role the INSERT
        // it needs to record what it did, AND the corrected PermissionCatalogUpdated
        // definition. Reconciling without it would commit and then fail to record.
        var missing = await AuditSchemaDeployer.MissingScriptsAsync(
            context.Database.GetDbConnection(), cancellationToken);

        if (missing.Count > 0)
        {
            error.WriteLine(
                $"The Audit schema is not deployed: {missing.Count} script(s) have "
                + $"not been applied ({string.Join(", ", missing)}). This tool records "
                + "what it does in the audit trail, which those scripts make possible. "
                + "Run, after the migrations:"
                + Environment.NewLine + Environment.NewLine
                + "  dotnet run --project src/Tools/Ligature.AuditSchema"
                + Environment.NewLine);

            return Failed;
        }

        var result = await new CatalogueSynchroniser(context, connectionString, Release)
            .SynchroniseAsync(new SystemClock().UtcNow, cancellationToken);

        return Report(result, output, error);
    }

    /// <summary>
    /// The outcome, mapped. Refused is NOT a failure: it means the tool
    /// successfully determined that the database violates the reconciliation
    /// policy, which is an expected and actionable deployment outcome.
    /// </summary>
    private static int Report(CatalogueSyncResult result, TextWriter output, TextWriter error)
    {
        switch (result.Outcome)
        {
            case CatalogueSyncOutcome.NotProvisioned:
                output.WriteLine(
                    "No System actor, so there is no catalogue to reconcile. This "
                    + "database has not been provisioned; first provision will seed "
                    + "it. Nothing was changed and nothing was recorded.");

                return Success;

            case CatalogueSyncOutcome.Succeeded:
                output.WriteLine("Catalogue synchronisation succeeded.");
                output.WriteLine($"  Permissions added:              {result.Counts.PermissionsInserted}");
                output.WriteLine($"  Permission metadata updated:    {result.Counts.PermissionMetadataReconciled}");
                output.WriteLine($"  Roles added:                    {result.Counts.RolesInserted}");
                output.WriteLine($"  Role metadata updated:          {result.Counts.RoleMetadataReconciled}");
                output.WriteLine($"  Grants added:                   {result.Counts.GrantsInserted}");

                return Success;

            default:
                // The subject-level detail lives HERE and not in the audit
                // payload, which carries reason codes and counts. This is where
                // whoever has to fix it is standing.
                error.WriteLine("Catalogue synchronisation refused.");
                error.WriteLine();

                foreach (var refusal in result.Refusals.OrderBy(x => x.Reason).ThenBy(x => x.Subject, StringComparer.Ordinal))
                    error.WriteLine($"  {refusal.Reason}: {refusal.Subject}");

                error.WriteLine();
                error.WriteLine("No catalogue mutations were committed.");

                return Refused;
        }
    }

    private static string Release
        => typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
}
