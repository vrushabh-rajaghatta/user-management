using Ligature.Platform.Application.Audit;
using Npgsql;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// The autonomous writer: its own connection, its own transaction, its own
/// commit.
///
/// A connection of its own is not an optimisation, it is the entire point.
/// Npgsql permits one transaction per connection, so borrowing the
/// DbContext's connection would mean either joining the command's
/// transaction — which is what this exists not to do — or waiting for it to
/// finish before a second one could begin. Opening a separate connection is
/// what makes "independently of the command" true rather than approximate.
///
/// The rows are written through the same INSERT the transactional writer
/// uses, so both paths produce identical records and only their commit
/// boundary differs.
/// </summary>
internal sealed class AutonomousAuditRecordWriter : IAutonomousAuditRecordWriter
{
    private readonly string _connectionString;

    public AutonomousAuditRecordWriter(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task WriteAsync(
        IReadOnlyList<AuditRecordRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return;

        await using var connection = new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await AuditRecordWriter.WriteAsync(connection, transaction, rows, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
