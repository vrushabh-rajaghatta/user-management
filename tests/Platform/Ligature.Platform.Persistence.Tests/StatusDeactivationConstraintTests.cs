using Ligature.Platform.Domain.Users;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-C4/C5 D12 (docs/requirements.md, "USR-C4 / USR-C5", M1): a row's
/// status and its deactivation stamp agree, on app_user and user_identity.
///
///     CHECK ((status = 'Inactive') = (deactivated_at IS NOT NULL))
///
/// The pair checks (AU4, UI9) already tie DeactivatedBy to DeactivatedAt, so
/// this closes the triangle: Inactive without a stamp, and Active with one,
/// are both refused by the database, whatever wrote them.
///
/// Raw SQL on purpose — the domain never produces either row, and the
/// constraint is what is under test. Every statement runs in a transaction
/// that is rolled back, so the shared database keeps nothing.
/// </summary>
public sealed class StatusDeactivationConstraintTests
{
    private static readonly string System = User.SystemUserId.Value.ToString();

    [Theory]
    [InlineData("Inactive", false)]
    [InlineData("Active", true)]
    public async Task A_user_whose_status_and_stamp_disagree_is_refused(string status, bool stamped)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InTransactionAsync(async (connection, transaction) =>
                await InsertUserAsync(connection, transaction, Guid.NewGuid(), status, stamped)));

        Assert.Equal("23514", failure.SqlState);
        Assert.Equal("ck_app_user_status_deactivation", failure.ConstraintName);
    }

    [Theory]
    [InlineData("Inactive", false)]
    [InlineData("Active", true)]
    public async Task An_identity_whose_status_and_stamp_disagree_is_refused(string status, bool stamped)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(
            () => InTransactionAsync(async (connection, transaction) =>
            {
                var user = Guid.NewGuid();
                await InsertUserAsync(connection, transaction, user, "Active", stamped: false);
                await InsertIdentityAsync(connection, transaction, user, status, stamped);
            }));

        Assert.Equal("23514", failure.SqlState);
        Assert.Equal("ck_user_identity_status_deactivation", failure.ConstraintName);
    }

    /// <summary>The check must not over-refuse: both consistent states are admitted.</summary>
    [Theory]
    [InlineData("Active", false)]
    [InlineData("Inactive", true)]
    public async Task Status_and_stamp_that_agree_are_admitted(string status, bool stamped)
        => await InTransactionAsync(async (connection, transaction) =>
        {
            var user = Guid.NewGuid();
            await InsertUserAsync(connection, transaction, user, status, stamped);
            await InsertIdentityAsync(connection, transaction, user, status, stamped);
        });

    // ------------------------------------------------------------ harness

    private static async Task InTransactionAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await body(connection, transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string status, bool stamped)
    {
        var unique = id.ToString("N");

        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO app_user (id, actor_type, first_name, last_name, display_name, email, status,
                                   created_at, created_by, updated_at, updated_by, deactivated_at, deactivated_by)
             VALUES ('{id}', 'Human', 'Status', 'Check', 'Status Check {unique[..8]}', 'status-{unique}@example.test',
                     '{status}', now(), '{System}', now(), '{System}',
                     {(stamped ? "now()" : "NULL")}, {(stamped ? $"'{System}'" : "NULL")});
             """, connection, transaction);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertIdentityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid user, string status, bool stamped)
    {
        var id = Guid.NewGuid();

        await using var command = new NpgsqlCommand(
            $"""
             INSERT INTO user_identity (id, user_id, actor_type, identity_type, identity_provider,
                                        subject_id, username, status, created_at, created_by,
                                        deactivated_at, deactivated_by)
             VALUES ('{id}', '{user}', 'Human', 'Local', 'Application', '{id}', 'status-{id:N}',
                     '{status}', now(), '{System}',
                     {(stamped ? "now()" : "NULL")}, {(stamped ? $"'{System}'" : "NULL")});
             """, connection, transaction);

        await command.ExecuteNonQueryAsync();
    }
}
