using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Domain.Users;
using Npgsql;

namespace Ligature.Platform.Persistence.Notifications;

/// <summary>
/// The eligibility gate, on its own connection.
///
/// One short read, evaluated immediately before transport and never cached
/// (N15). A connection of its own rather than the DbContext's, for the same
/// reason the autonomous audit writer takes one: this runs on a background
/// thread with no request scope, and must not enlist in anybody's transaction.
///
/// The two conditions are evaluated as one statement so they describe the same
/// instant. Reading them separately would leave a window in which the token
/// expired between the two halves of a single decision.
/// </summary>
internal sealed class NotificationGate : INotificationGate
{
    private readonly string _connectionString;

    internal NotificationGate(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
    }

    public async Task<NotificationEligibility> EvaluateAsync(
        UserTokenId tokenId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenId);

        await using var connection = new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                (t.used_at IS NULL
                 AND t.invalidated_at IS NULL
                 AND t.expires_at > now())          AS token_live,
                (i.status = 'Active'
                 AND u.status = 'Active')           AS subject_active
            FROM notification n
            JOIN user_token   t ON t.id = n.token_id
            JOIN user_identity i ON i.id = t.user_identity_id
            JOIN app_user      u ON u.id = i.user_id
            WHERE t.id = @token
            """,
            connection);

        command.Parameters.AddWithValue("token", tokenId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // No row means the token is gone, which cannot happen while a
        // notification references it — the foreign key sees to that. Treated as
        // not live rather than as a defect: the outcome is the same, and a
        // sender that threw here would leave the row Pending for the sweeper
        // to call Abandoned, which would be less true than TokenNotLive.
        if (!await reader.ReadAsync(cancellationToken))
            return NotificationEligibility.TokenNotLive;

        // Token first, deliberately. When both fail this reports TokenNotLive,
        // because that is the condition making the message useless rather than
        // merely inappropriate.
        if (!reader.GetBoolean(0))
            return NotificationEligibility.TokenNotLive;

        return reader.GetBoolean(1)
            ? NotificationEligibility.Eligible
            : NotificationEligibility.SubjectInactive;
    }
}
