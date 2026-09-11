using Ligature.Platform.Application.Notifications;
using Npgsql;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// NOT-P3. Closes rows the sender was never observed to begin.
///
/// This is what gives the lifecycle a terminus. Without it a Pending row would
/// wait for ever for a sender that died, was shed at the handoff, or — on a
/// host with no mail configured — never existed at all. It runs in both of the
/// pump's modes for exactly that reason: abandonment is not a delivery concern.
///
/// ATTEMPTED_AT IS LEFT ALONE, and that is the whole meaning of Abandoned. No
/// attempt was observed, so no attempt time is claimed — which is also what
/// distinguishes this from TransportFailed, where we know it did not go. It
/// deliberately covers the case where the transport HAD accepted the message
/// and the process died before the terminal write: the mail went, nobody wrote
/// it down, and the row says so rather than guessing.
///
/// Idempotent by construction: a swept row no longer matches the predicate.
/// Batched with a bounded batch, each batch its own short transaction, using
/// the partial index shipped with the table.
/// </summary>
internal sealed class NotificationSweeper : INotificationSweeper
{
    private readonly string _connectionString;

    internal NotificationSweeper(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task<int> SweepAsync(
        TimeSpan graceWindow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        // The subquery is what bounds the batch. Both sides of the age
        // comparison come from the database clock, which is why created_at is
        // defaulted by the database rather than stamped by the application.
        await using var command = new NpgsqlCommand(
            """
            UPDATE notification
            SET status          = 'NotSent',
                not_sent_reason = 'Abandoned',
                closed_at       = now()
            WHERE id IN (
                SELECT id
                FROM notification
                WHERE status = 'Pending'
                  AND created_at < now() - @grace
                ORDER BY created_at
                LIMIT @batch)
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("grace", graceWindow);
        command.Parameters.AddWithValue("batch", batchSize);

        var swept = await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return swept;
    }
}
