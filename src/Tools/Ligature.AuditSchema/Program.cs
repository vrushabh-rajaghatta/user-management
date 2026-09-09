using Ligature.Platform.Persistence.Audit;

namespace Ligature.AuditSchema;

/// <summary>
/// Deploys the Audit schema and its tamper boundary, then exits.
///
/// A separate tool because it runs under a credential neither the migrator
/// nor the provisioner may hold, and must be unreachable from the host — the
/// same dependency-isolation argument that keeps Ligature.Provisioning
/// outside the host application (docs/architecture.md section 4).
///
/// It is NOT a general migration runner and must not become one. Ordinary
/// application schema stays with EF migrations; this tool exists solely
/// because audit objects must be owned by a role that nothing can
/// authenticate as, and EF cannot create objects owned by anyone but the
/// role it connects as.
///
/// Idempotent: a second run applies nothing and says so.
/// </summary>
internal static class Program
{
    internal const string ConnectionVariable = "LIGATURE_PRIVILEGED_CONNECTION";

    internal const int Success = 0;
    internal const int UsageError = 1;
    internal const int DeploymentFailed = 2;

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            WriteUsage(Console.Out);

            return Success;
        }

        if (args.Length > 0)
        {
            Console.Error.WriteLine(
                $"Unrecognised argument(s): {string.Join(' ', args)}");
            Console.Error.WriteLine();

            WriteUsage(Console.Error);

            return UsageError;
        }

        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // No default, for the reason docs/architecture.md section 17
            // gives about the signing key: a fallback would choose a target
            // on the operator's behalf, and the target here is the audit
            // trail of a customer database.
            Console.Error.WriteLine(
                $"{ConnectionVariable} is not set. It must name a connection "
                + "with authority to create a schema owned by another role — "
                + "in practice the cluster administrator. This tool holds no "
                + "credentials and has no default.");

            return UsageError;
        }

        try
        {
            var result = await new AuditSchemaDeployer(connectionString)
                .DeployAsync();

            if (result.Applied.Count == 0)
            {
                Console.WriteLine(
                    "The Audit schema is already current. "
                    + $"{result.AlreadyCurrent.Count} script(s) previously "
                    + "applied; nothing changed.");

                return Success;
            }

            foreach (var script in result.Applied)
            {
                Console.WriteLine($"applied  {script}");
            }

            Console.WriteLine();
            Console.WriteLine(
                $"Audit schema deployed. Objects in the '{AuditSchemaDeployer.Schema}' "
                + $"schema are owned by {AuditSchemaDeployer.OwnerRole}, which cannot "
                + "log in and has no members.");

            return Success;
        }
        catch (AuditSchemaDeploymentException failure)
        {
            Console.Error.WriteLine(failure.Message);

            return DeploymentFailed;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"Audit schema deployment failed: {failure.Message}");

            return DeploymentFailed;
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Deploys the Audit schema and its tamper boundary.");
        writer.WriteLine();
        writer.WriteLine("  dotnet run --project src/Tools/Ligature.AuditSchema");
        writer.WriteLine();
        writer.WriteLine($"Requires {ConnectionVariable}, a connection with authority to");
        writer.WriteLine("create a schema owned by another role. Run it AFTER the EF");
        writer.WriteLine("migrations: the Audit foreign keys reference app_user, role and");
        writer.WriteLine("user_role, which must already exist.");
        writer.WriteLine();
        writer.WriteLine("Takes no arguments and is idempotent.");
    }
}
