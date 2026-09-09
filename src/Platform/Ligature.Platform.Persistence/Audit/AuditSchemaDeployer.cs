using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// Deploys the Audit schema and its tamper boundary.
///
/// Audit DDL is NOT an EF migration, and this class is why. Whoever runs
/// CREATE TABLE owns the table, and an owner can disable triggers and drop
/// objects regardless of any GRANT. EF migrations run as migration_role, so
/// EF-created audit tables would be owned — and therefore destroyable — by
/// the migration credential. Probing confirmed exactly that: with the tables
/// in `public`, migration_role dropped two of them.
///
/// So the objects are created by a privileged connection that issues
/// SET ROLE audit_owner first, inside a schema owned by audit_owner. They are
/// born owned by a role nobody can authenticate as and nobody is a member of.
/// Ownership is never transferred, because a transfer implies an interval in
/// which something else owned the trail.
///
/// This class is deliberately not a general migration runner. It applies the
/// Audit scripts and nothing else; ordinary application schema remains EF's.
/// </summary>
public sealed class AuditSchemaDeployer
{
    /// <summary>The schema every Audit object lives in.</summary>
    public const string Schema = "audit";

    /// <summary>Owns the schema and every object in it. NOLOGIN, no members.</summary>
    public const string OwnerRole = "audit_owner";

    /// <summary>Holds the AR20 column-level UPDATE grant. NOLOGIN until slice D.</summary>
    public const string AnonymiserRole = "audit_anonymiser";

    /// <summary>
    /// Roles this deployment grants to, and therefore requires to exist. They
    /// belong to the database foundation rather than to Audit, so the
    /// deployer verifies them and refuses rather than inventing them — a role
    /// it created would need a password, and this tool holds no secrets.
    /// </summary>
    private static readonly string[] RequiredRoles =
        ["app_role", "migration_role", "provisioning_role"];

    /// <summary>
    /// User Management tables the Audit foreign keys point at (AR10, AR12).
    /// Creating a foreign key needs REFERENCES on the target, and these are
    /// owned by migration_role, so the privileged prelude grants it.
    /// </summary>
    private static readonly string[] ReferencedTables =
        ["app_user", "role", "user_role"];

    private readonly string _privilegedConnectionString;

    public AuditSchemaDeployer(string privilegedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privilegedConnectionString);

        _privilegedConnectionString = privilegedConnectionString;
    }

    /// <summary>
    /// Applies every script not yet recorded in the ledger, in name order.
    /// Idempotent: a second run applies nothing and reports so.
    /// </summary>
    public async Task<AuditSchemaDeploymentResult> DeployAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(_privilegedConnectionString);

        await connection.OpenAsync(cancellationToken);

        await VerifyRequiredRolesAsync(connection, cancellationToken);
        await PrepareOwnershipAsync(connection, cancellationToken);

        // Everything from here is audit_owner's, so every object created
        // below is owned by it from the moment it exists.
        await ExecuteAsync(connection, $"SET ROLE {OwnerRole};", cancellationToken);

        await EnsureLedgerAsync(connection, cancellationToken);

        var applied = new List<string>();
        var alreadyCurrent = new List<string>();

        foreach (var script in LoadScripts())
        {
            if (await IsAppliedAsync(connection, script, cancellationToken))
            {
                alreadyCurrent.Add(script.Name);

                continue;
            }

            await ApplyAsync(connection, script, cancellationToken);

            applied.Add(script.Name);
        }

        // Applied is not the same as correct. Verify what was built before
        // reporting success, so a deployment stops here rather than handing
        // the migrator and provisioner a boundary that only looks right.
        await AuditConstructionVerification.VerifyAsync(connection, cancellationToken);

        return new AuditSchemaDeploymentResult(applied, alreadyCurrent);
    }

    /// <summary>The scripts a current release expects the ledger to hold, in order.</summary>
    public static IReadOnlyList<string> ExpectedScriptNames
        => LoadScripts().Select(x => x.Name).ToArray();

    /// <summary>
    /// Which of the release's scripts the target database has NOT recorded.
    /// Ligature.Provisioning uses this as a precondition, in the same way it
    /// refuses to run with pending EF migrations: a missing-table error from
    /// inside a seed would not say what was actually wrong. Opens the
    /// connection if the caller has not.
    /// </summary>
    public static async Task<IReadOnlyList<string>> MissingScriptsAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var expected = ExpectedScriptNames;

        await using var command = connection.CreateCommand();

        command.CommandText =
            $"SELECT script_name FROM {Schema}.audit_schema_version "
            + "WHERE to_regclass('" + Schema + ".audit_schema_version') IS NOT NULL";

        var applied = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                applied.Add(reader.GetString(0));
        }
        catch (Npgsql.PostgresException failure)
            when (failure.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable
               || failure.SqlState == Npgsql.PostgresErrorCodes.InvalidSchemaName)
        {
            // No ledger at all: nothing has been deployed.
        }

        return expected.Where(x => !applied.Contains(x)).ToArray();
    }

    /// <summary>
    /// Fails with the missing names rather than with whatever a GRANT to a
    /// non-existent role says, which is not obviously about roles at all.
    /// </summary>
    private static async Task VerifyRequiredRolesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT rolname FROM pg_roles WHERE rolname = ANY(@names)",
            connection);

        command.Parameters.AddWithValue("names", RequiredRoles);

        var present = new List<string>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                present.Add(reader.GetString(0));
            }
        }

        var missing = RequiredRoles.Except(present, StringComparer.Ordinal).ToArray();

        if (missing.Length > 0)
        {
            throw new AuditSchemaDeploymentException(
                $"These database roles do not exist: {string.Join(", ", missing)}. "
                + "They are created by the database foundation step, which runs "
                + "before this one, because they need credentials this tool does "
                + "not hold. See AGENTS.md section 3.");
        }
    }

    /// <summary>
    /// The privileged prelude, run BEFORE SET ROLE because each statement
    /// needs authority audit_owner does not have: creating a schema owned by
    /// another role, and granting REFERENCES on tables owned by
    /// migration_role.
    /// </summary>
    private static async Task PrepareOwnershipAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        // NOLOGIN and no password: there is no credential to leak, and no
        // way to authenticate as either. audit_anonymiser gains a credential
        // only when the erasure worker is built (IMPL-11).
        await ExecuteAsync(connection, $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                    CREATE ROLE {OwnerRole} NOLOGIN;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_roles WHERE rolname = '{AnonymiserRole}') THEN
                    CREATE ROLE {AnonymiserRole} NOLOGIN;
                END IF;
            END $$;
            """, cancellationToken);

        await ExecuteAsync(
            connection,
            $"CREATE SCHEMA IF NOT EXISTS {Schema} AUTHORIZATION {OwnerRole};",
            cancellationToken);

        await ExecuteAsync(
            connection,
            $"GRANT USAGE ON SCHEMA public TO {OwnerRole};",
            cancellationToken);

        foreach (var table in ReferencedTables)
        {
            await ExecuteAsync(
                connection,
                $"GRANT REFERENCES ON public.\"{table}\" TO {OwnerRole};",
                cancellationToken);
        }
    }

    /// <summary>
    /// The ledger replaces __EFMigrationsHistory for these objects. Losing
    /// EF's history was the accepted cost of keeping migration_role away from
    /// ownership; losing release traceability was not, so it is recorded here
    /// and doubles as installation-qualification evidence.
    /// </summary>
    private static async Task EnsureLedgerAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
        => await ExecuteAsync(connection, $"""
            CREATE TABLE IF NOT EXISTS {Schema}.audit_schema_version
            (
                script_name varchar(200) NOT NULL,
                checksum    varchar(64)  NOT NULL,
                applied_at  timestamptz  NOT NULL DEFAULT now(),

                CONSTRAINT pk_audit_schema_version PRIMARY KEY (script_name)
            );

            -- Provisioning refuses to seed against a schema whose scripts are
            -- not all applied, which means reading this ledger. Granted here,
            -- with the table it belongs to, so the grant cannot be skipped.
            GRANT SELECT ON {Schema}.audit_schema_version TO provisioning_role;
            """, cancellationToken);

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        AuditSchemaScript script,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT checksum FROM {Schema}.audit_schema_version "
            + "WHERE script_name = @name",
            connection);

        command.Parameters.AddWithValue("name", script.Name);

        var recorded = await command.ExecuteScalarAsync(cancellationToken) as string;

        if (recorded is null)
        {
            return false;
        }

        // An edited script that has already been deployed is a release
        // problem, not something to silently re-apply or silently skip: the
        // deployed database and the source no longer describe the same trail.
        if (!string.Equals(recorded, script.Checksum, StringComparison.Ordinal))
        {
            throw new AuditSchemaDeploymentException(
                $"'{script.Name}' was already applied with checksum {recorded}, "
                + $"but the script in this release has checksum {script.Checksum}. "
                + "Audit schema scripts are immutable once deployed; a change "
                + "ships as a new numbered script.");
        }

        return true;
    }

    private static async Task ApplyAsync(
        NpgsqlConnection connection,
        AuditSchemaScript script,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (var command =
            new NpgsqlCommand(script.Sql, connection, transaction))
        {
            command.CommandTimeout = 0;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var record = new NpgsqlCommand(
            $"INSERT INTO {Schema}.audit_schema_version (script_name, checksum) "
            + "VALUES (@name, @checksum)",
            connection,
            transaction))
        {
            record.Parameters.AddWithValue("name", script.Name);
            record.Parameters.AddWithValue("checksum", script.Checksum);

            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Embedded rather than read from disk, so the deployed image carries the
    /// exact scripts the release was built from and cannot be pointed at
    /// someone else's.
    /// </summary>
    internal static IReadOnlyList<AuditSchemaScript> LoadScripts()
    {
        var assembly = typeof(AuditSchemaDeployer).Assembly;

        const string prefix = "Ligature.Platform.Persistence.Audit.Schema.";

        var names = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith(prefix, StringComparison.Ordinal)
                     && x.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
        {
            throw new AuditSchemaDeploymentException(
                "No Audit schema scripts are embedded in "
                + $"{assembly.GetName().Name}. The .sql files must be included "
                + "as EmbeddedResource.");
        }

        return names.Select(x => Read(assembly, x, prefix)).ToArray();
    }

    private static AuditSchemaScript Read(
        Assembly assembly,
        string resourceName,
        string prefix)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new AuditSchemaDeploymentException(
                $"Embedded resource '{resourceName}' could not be opened.");

        using var reader = new StreamReader(stream, Encoding.UTF8);

        var sql = reader.ReadToEnd();

        // Hash line-ending-normalised content. Git may check these files out
        // with CRLF on one machine and LF on another, and a checksum that
        // changed with the checkout would report every already-deployed
        // script as edited.
        var canonical = sql.Replace("\r\n", "\n").Replace("\r", "\n");

        var checksum = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        return new AuditSchemaScript(
            resourceName[prefix.Length..],
            sql,
            checksum);
    }
}

internal sealed record AuditSchemaScript(string Name, string Sql, string Checksum);

/// <summary>What was applied, and what was already current.</summary>
public sealed record AuditSchemaDeploymentResult(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> AlreadyCurrent);

public sealed class AuditSchemaDeploymentException : Exception
{
    public AuditSchemaDeploymentException(string message)
        : base(message)
    {
    }
}
