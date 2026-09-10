using Ligature.Platform.Persistence.Provisioning;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// A provisioned database of this class's own, created once for the class and
/// dropped when it finishes (E2A-T01).
///
/// Activation is one-shot per identity: a token can be consumed once, so every
/// run needs a brand new user, and once that user activates they are the ACTOR
/// of three audit records. A record's actor is a foreign key to app_user, and
/// no role may delete an audit row, so the users these tests create can never
/// be cleaned up. Pointed at the shared integration database, every run would
/// leave a user, an identity, a credential and three permanent records behind,
/// for as long as the database lived.
///
/// The referential permanence is deliberate and is not weakened to keep a test
/// database tidy. The tests move instead. Nothing here touches the shared
/// database, and nothing accumulates in it.
///
/// One database per class rather than per test: provisioning is the expensive
/// part, the tests seed their own users, and none of them cares what the others
/// left behind.
/// </summary>
public sealed class ActivationDatabase : IAsyncLifetime
{
    private ThrowawayDatabase? _database;

    internal string ConnectionString
        => _database?.ConnectionString
            ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        _database = await ThrowawayDatabase.CreateAsync();

        await using var context = _database.CreateContext();

        await new PlatformProvisioner(context).ProvisionAsync(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    internal async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);

        await connection.OpenAsync();

        return connection;
    }
}
