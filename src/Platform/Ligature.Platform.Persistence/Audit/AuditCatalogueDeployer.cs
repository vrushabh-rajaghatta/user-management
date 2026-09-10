using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// Deploys the release's audit event catalogue, in the deployment phase,
/// before the host starts.
///
/// This exists because of IMPL-08. The host verifies its compiled audit
/// declarations against the deployed catalogue at startup and refuses to run
/// if they disagree — deliberately, because a host that started anyway would
/// fail on the first affected command, in production, with a 500. That
/// verification makes the catalogue a PRECONDITION of the host, so it cannot
/// be written by a bootstrap step that runs after the host is already up.
///
/// It is release-controlled infrastructure and it is versioned with the code
/// that declares it, which is why it belongs here and not with the tenant
/// seed data that PRV-C1 writes.
///
/// The public surface is this class; the catalogue definitions and the writer
/// stay internal. A caller cannot reach in and deploy some other catalogue.
/// </summary>
public sealed class AuditCatalogueDeployer
{
    private readonly string _privilegedConnectionString;

    /// <summary>
    /// The same privileged connection the schema deployer uses. The catalogue
    /// tables are owned by audit_owner, and after this change no application
    /// role holds INSERT on them at all — which is the point: release data is
    /// written by the release, not by the running system.
    /// </summary>
    public AuditCatalogueDeployer(string privilegedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privilegedConnectionString);

        _privilegedConnectionString = privilegedConnectionString;
    }

    /// <summary>
    /// One transaction for the whole catalogue. A partially reconciled
    /// catalogue would be a catalogue the host might verify successfully
    /// against while still missing an entry a handler declares, so there is
    /// no useful half-way state to commit.
    /// </summary>
    public async Task<AuditCatalogueDeploymentResult> DeployAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_privilegedConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await new AuditCatalogueSeeder(connection, transaction)
            .ReconcileCatalogueAsync(cancellationToken);

        var result = await SummariseAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Read back inside the transaction, so the numbers reported are the ones
    /// about to be committed rather than a second query's opinion.
    /// </summary>
    private static async Task<AuditCatalogueDeploymentResult> SummariseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT count(*) FROM audit.audit_event_type),
                (SELECT count(*) FROM audit.audit_event_type WHERE is_active),
                (SELECT count(*) FROM audit.audit_event_origin),
                (SELECT count(*) FROM audit.audit_event_origin WHERE is_active)
            """,
            connection,
            transaction);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        await reader.ReadAsync(cancellationToken);

        return new AuditCatalogueDeploymentResult(
            EventTypes: (int)reader.GetInt64(0),
            ActiveEventTypes: (int)reader.GetInt64(1),
            Origins: (int)reader.GetInt64(2),
            ActiveOrigins: (int)reader.GetInt64(3));
    }
}

/// <summary>
/// Totals rather than a change list. The catalogue is reconciled to the
/// release on every deployment, so "what it now contains" is the fact worth
/// reporting; retired entries are the difference between the two counts.
/// </summary>
public sealed record AuditCatalogueDeploymentResult(
    int EventTypes,
    int ActiveEventTypes,
    int Origins,
    int ActiveOrigins);
