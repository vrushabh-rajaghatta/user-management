using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Notifications;
using Npgsql;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// The single guarded UPDATE that closes a notification, on its own connection
/// and its own short transaction, never enlisting in an ambient one.
///
/// THE RETRY AND N9 ARE ONE MECHANISM. A transient database failure leaves us
/// unable to tell whether the write committed. Retrying is safe only because
/// the N9 trigger refuses any update of a row that is no longer Pending: if the
/// first attempt did commit, the second is refused, and that refusal PROVES the
/// row already reached the state we wanted. So it is treated as success rather
/// than as an error. Without N9 this retry would be a way to overwrite a
/// terminal record.
///
/// Only genuinely transient failures are retried. A PostgresException is the
/// server answering — a constraint, a trigger, a privilege — and answering
/// twice will not change its mind.
///
/// If the write ultimately fails the row stays Pending, and the sweeper closes
/// it as Abandoned. That is the honest record: the outcome was never persisted,
/// so the system does not claim to know it.
/// </summary>
internal sealed class NotificationTerminalWriter : INotificationTerminalWriter
{
    private readonly string _connectionString;
    private readonly TimeProvider _time;

    internal NotificationTerminalWriter(
        string connectionString,
        TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _time = time;
    }

    public async Task CloseAsync(
        NotificationId notificationId,
        NotificationOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notificationId);
        ArgumentNullException.ThrowIfNull(outcome);

        // The application clock at the instant the terminal update is issued
        // (§3.2), stamped here because here is that instant.
        var closedAt = _time.GetUtcNow();

        try
        {
            await WriteAsync(notificationId, outcome, closedAt, cancellationToken);
        }
        catch (NpgsqlException transient) when (transient is not PostgresException)
        {
            try
            {
                await WriteAsync(notificationId, outcome, closedAt, cancellationToken);
            }
            catch (PostgresException refusal)
                when (refusal.SqlState == PostgresErrorCodes.RaiseException)
            {
                // N9 refused it, so the row is already terminal — which means
                // the first write did commit and we simply never heard so.
            }
        }
    }

    private async Task WriteAsync(
        NotificationId notificationId,
        NotificationOutcome outcome,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            UPDATE notification
            SET status               = @status,
                not_sent_reason      = @reason,
                attempted_at         = @attempted,
                closed_at            = @closed,
                transport_message_id = @message
            WHERE id = @id
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("id", notificationId.Value);
        command.Parameters.AddWithValue("status", outcome.Status.ToString());
        command.Parameters.AddWithValue("attempted", outcome.AttemptedAt);
        command.Parameters.AddWithValue("closed", closedAt);

        command.Parameters.AddWithValue(
            "reason",
            outcome.Reason is null
                ? DBNull.Value
                : outcome.Reason.Value.ToString());

        command.Parameters.AddWithValue(
            "message",
            outcome.TransportMessageId is null
                ? DBNull.Value
                : outcome.TransportMessageId);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
