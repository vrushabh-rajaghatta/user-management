using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-C1 step 9 end to end: the handler declares, the pipeline writes the
/// Pending row inside the command's transaction, and the plaintext goes nowhere
/// near the database.
///
/// Phase B of slice N1 stops at the declaration path. Nothing here sends
/// anything, because nothing yet can: the bounded handoff and the sender arrive
/// in Phases C and D. What these tests establish is the property those phases
/// will depend on — that a declaration belongs to exactly one command attempt,
/// commits with the token it names, and disappears with the command.
/// </summary>
public sealed class NotificationEnqueueIntegrationTests
{
    [Fact]
    public async Task An_activation_notification_commits_with_the_user()
    {
        await RunAsync(async (dispatcher, _, __) =>
        {
            var command = NewCommand();

            var result = await dispatcher
                .SendAsync<CreateUserCommand, CreateUserResult>(
                    command, CancellationToken.None);

            var row = await ReadNotificationAsync(result.UserIdentityId);

            Assert.NotNull(row);
            Assert.Equal("AccountActivation", row!.Value.Type);
            Assert.Equal("Pending", row.Value.Status);
            Assert.Equal(command.Email, row.Value.Recipient);

            // The row names the token the same transaction issued.
            var tokenId = await ReadTokenIdAsync(result.UserIdentityId);
            Assert.Equal(tokenId, row.Value.TokenId);

            // Pending carries no terminal state at all (the §4.1 shape).
            Assert.Null(row.Value.NotSentReason);
            Assert.Null(row.Value.AttemptedAt);
            Assert.Null(row.Value.ClosedAt);
            Assert.Null(row.Value.TransportMessageId);
        });
    }

    [Fact]
    public async Task The_collector_holds_nothing_once_the_command_has_returned()
    {
        await RunAsync(async (dispatcher, _, services) =>
        {
            await dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
                NewCommand(), CancellationToken.None);

            // The post-commit behaviour's command lifetime has closed, taking
            // the declaration — and the last reference to the token plaintext
            // — with it. This is what "discard" means for a CLR string: no
            // live reference. It is not memory erasure and does not claim to
            // be.
            var scope = services.GetRequiredService<INotificationEmissionScope>();

            Assert.Empty(scope.Declarations);
        });
    }

    /// <summary>
    /// N16, structurally. The mapped column list is the whole of what can be
    /// written, so a plaintext, a rendered body or a URL has nowhere to go.
    /// Adding a column to the entity fails this test before it can reach a
    /// migration.
    /// </summary>
    [Fact]
    public void The_notification_mapping_has_no_column_a_plaintext_could_occupy()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider
            .GetRequiredService<LigatureDbContext>();

        var columns = context.Model
            .FindEntityType(typeof(Notification))!
            .GetProperties()
            .Select(property => property.GetColumnName())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "attempted_at",
                "closed_at",
                "created_at",
                "id",
                "not_sent_reason",
                "notification_type",
                "recipient",
                "status",
                "token_id",
                "transport_message_id",
            ],
            columns);
    }

    [Fact]
    public async Task A_failed_transaction_leaves_no_notification()
    {
        await RunAsync(
            failFirstNotificationSave: true,
            body: async (dispatcher, _, services) =>
            {
                var command = NewCommand();

                await Assert.ThrowsAnyAsync<Exception>(
                    () => dispatcher.SendAsync<CreateUserCommand, CreateUserResult>(
                        command, CancellationToken.None));

                // The row and the token roll back together, so there is no
                // notification for a token that does not exist.
                Assert.Equal(0, await CountNotificationsForAsync(command.Email));
                Assert.Equal(0, await CountUsersForAsync(command.Email));

                // The command lifetime closes on the throw path too, so the
                // declaration — and the plaintext it carried — is dropped
                // whether the command succeeded or failed.
                Assert.Empty(services
                    .GetRequiredService<INotificationEmissionScope>()
                    .Declarations);
            });
    }

    // ------------------------------------------------------------------
    // Forcing a replay
    // ------------------------------------------------------------------

    private sealed class SentinelException : Exception
    {
        internal SentinelException()
            : base("Injected transient failure.")
        {
        }
    }

    /// <summary>
    /// Throws once, on the first save that would commit a notification — which
    /// is the outer SaveChanges, after the handler has declared and the
    /// emission behaviour has added the row. Failing earlier would roll back
    /// before a declaration existed and would prove nothing.
    /// </summary>
    private sealed class FailFirstNotificationSave : SaveChangesInterceptor
    {
        private bool _thrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_thrown
                && eventData.Context is not null
                && eventData.Context.ChangeTracker.Entries<Notification>().Any())
            {
                _thrown = true;

                throw new SentinelException();
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // ------------------------------------------------------------------
    // Fixture plumbing
    // ------------------------------------------------------------------

    private static string ConnectionString => TestDatabase.ConnectionString;

    private static async Task RunAsync(
        Func<ICommandDispatcher, UserId, IServiceProvider, Task> body)
        => await RunAsync(false, body);

    private static async Task RunAsync(
        bool failFirstNotificationSave,
        Func<ICommandDispatcher, UserId, IServiceProvider, Task> body)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var administrator = (await PermanentTestCaller.EnsureAsync(
            ConnectionString, "user-administrator")).UserId;

        await using var provider = BuildProvider(failFirstNotificationSave);

        using var scope = provider.CreateScope();

        scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(administrator, ActorType.Human, TestActorIdentity.Human());

        await body(
            scope.ServiceProvider.GetRequiredService<ICommandDispatcher>(),
            administrator,
            scope.ServiceProvider);
    }

    private static ServiceProvider BuildProvider(
        bool failFirstNotificationSave = false)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(ConnectionString);

        if (failFirstNotificationSave)
        {
            services.AddScoped<FailFirstNotificationSave>();

            // Replaces the persistence module's registration for this test
            // only. The provenance interceptor is re-added deliberately:
            // app_user.updated_at and updated_by are NOT NULL shadow
            // properties that nothing else populates.
            services.AddDbContext<LigatureDbContext>((sp, options) =>
            {
                options.UseNpgsql(ConnectionString);

                options.AddInterceptors(
                    sp.GetRequiredService<ProvenanceStampingInterceptor>());

                if (failFirstNotificationSave)
                {
                    options.AddInterceptors(
                        sp.GetRequiredService<FailFirstNotificationSave>());
                }
            });
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CreateUserCommand NewCommand()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        return new CreateUserCommand(
            "Notified",
            "Person",
            $"Notified Person {discriminator[..8]}",
            $"notified-{discriminator}@example.test",
            $"notified-{discriminator[..12]}");
    }

    private readonly record struct NotificationRow(
        string Type,
        string Status,
        string Recipient,
        Guid TokenId,
        string? NotSentReason,
        DateTimeOffset? AttemptedAt,
        DateTimeOffset? ClosedAt,
        string? TransportMessageId);

    private static async Task<NotificationRow?> ReadNotificationAsync(
        UserIdentityId identityId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT n.notification_type, n.status, n.recipient, n.token_id,
                   n.not_sent_reason, n.attempted_at, n.closed_at,
                   n.transport_message_id
            FROM notification n
            JOIN user_token t ON t.id = n.token_id
            WHERE t.user_identity_id = @id
            """,
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new NotificationRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    private static async Task<Guid> ReadTokenIdAsync(UserIdentityId identityId)
        => await ScalarAsync<Guid>(
            "SELECT id FROM user_token WHERE user_identity_id = @id",
            "id", identityId.Value);

    private static async Task<int> CountNotificationsForAsync(string email)
        => (int)await ScalarAsync<long>(
            """
            SELECT count(*)
            FROM notification n
            JOIN user_token t ON t.id = n.token_id
            JOIN user_identity i ON i.id = t.user_identity_id
            JOIN app_user u ON u.id = i.user_id
            WHERE u.email = @email
            """,
            "email", email);

    private static async Task<int> CountUsersForAsync(string email)
        => (int)await ScalarAsync<long>(
            "SELECT count(*) FROM app_user WHERE email = @email",
            "email", email);

    private static async Task<T> ScalarAsync<T>(
        string sql, string parameterName, object value)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(parameterName, value);

        return (T)(await command.ExecuteScalarAsync())!;
    }
}
