using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ligature.Host.Configuration;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Host.Tests;

/// <summary>
/// The development mail sink through the real host (docs/architecture.md §8):
/// create a user over HTTP, and the activation link lands in the sink directory,
/// carrying the credential the database holds the hash of, while the
/// notification row records where it went.
///
/// The link is proven to be the REAL one by hashing the secret it carries and
/// matching the stored token_hash — not by activating with it. Activation would
/// make the new user the actor of an audit record, and a user who has acted can
/// never be deleted (docs/architecture.md §11), so this suite could not clean up.
/// </summary>
public sealed partial class DevelopmentMailSinkHostTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private const string BaseUrl = "https://localhost:5173";

    private static readonly (Guid User, Guid Identity) Administrator =
        (Guid.Parse("de5c0000-0000-4000-8000-00000000da01"),
         Guid.Parse("de5c0000-0000-4000-8000-00000000da02"));

    private readonly string _directory = Directory.CreateTempSubdirectory("ligature-host-sink-").FullName;

    [Fact]
    public async Task Creating_a_user_writes_their_real_activation_link_to_the_sink()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory(settings: new Dictionary<string, string>
        {
            [HostConfiguration.PublicBaseUrlSetting] = BaseUrl,
            [HostConfiguration.MailDevSinkDirectorySetting] = _directory,
        });

        var client = factory.CreateClient();
        var admin = await EnsureAdministratorAsync(client);

        var marker = Guid.NewGuid().ToString("N")[..10];
        var email = $"sink-{marker}@example.test";

        Guid? created = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
            {
                Content = JsonContent.Create(new
                {
                    FirstName = "Sink",
                    LastName = "Recipient",
                    DisplayName = $"Sink Recipient {marker}",
                    Email = email,
                    InitialUsername = $"sink-{marker}",
                }),
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            created = body.RootElement.GetProperty("userId").GetGuid();
            var identity = body.RootElement.GetProperty("userIdentityId").GetGuid();

            // The send happens after the response, on the notification pump.
            var file = await WaitForFileAsync();
            var content = await File.ReadAllTextAsync(file);

            Assert.Contains($"To: {email}", content, StringComparison.Ordinal);

            var link = ActivationLink().Match(content);
            Assert.True(link.Success, "The written message carries no activation link on the configured base URL.");

            var (tokenId, secret) = (Guid.Parse(link.Groups["id"].Value), link.Groups["secret"].Value);

            await using var connection = await TestDatabase.OpenAsync();

            await using (var token = new NpgsqlCommand(
                "SELECT token_hash, token_type FROM user_token WHERE id = @id AND user_identity_id = @identity",
                connection))
            {
                token.Parameters.AddWithValue("id", tokenId);
                token.Parameters.AddWithValue("identity", identity);

                await using var reader = await token.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync(), "The link names no token of the created identity.");
                Assert.Equal(Sha256Hex(secret), reader.GetString(0));
                Assert.Equal("Activation", reader.GetString(1));
            }

            await using (var notification = new NpgsqlCommand(
                "SELECT status, transport_message_id FROM notification WHERE token_id = @id", connection))
            {
                notification.Parameters.AddWithValue("id", tokenId);

                await using var reader = await notification.ExecuteReaderAsync();

                Assert.True(await reader.ReadAsync());
                Assert.Equal("Sent", reader.GetString(0));
                Assert.Equal($"dev-sink:{Path.GetFileName(file)}", reader.GetString(1));
            }
        }
        finally
        {
            if (created is not null)
                await DeleteUserAsync(created.Value);
        }
    }

    /// <summary>
    /// CRD-C7 through the real host: reissuing writes a SECOND message, whose
    /// link carries the credential of the one open activation token. The first
    /// link's token is invalidated. Proven by hash, not by activating, for the
    /// cleanup reason above.
    /// </summary>
    [Fact]
    public async Task Reissuing_writes_a_second_message_carrying_the_one_open_link()
    {
        await TestDatabase.EnsureProvisionedAsync();

        await using var factory = new HostFactory(settings: new Dictionary<string, string>
        {
            [HostConfiguration.PublicBaseUrlSetting] = BaseUrl,
            [HostConfiguration.MailDevSinkDirectorySetting] = _directory,
        });

        var client = factory.CreateClient();
        var admin = await EnsureAdministratorAsync(client);

        var marker = Guid.NewGuid().ToString("N")[..10];

        Guid? created = null;

        try
        {
            using var create = new HttpRequestMessage(HttpMethod.Post, "/api/users")
            {
                Content = JsonContent.Create(new
                {
                    FirstName = "Sink",
                    LastName = "Reissued",
                    DisplayName = $"Sink Reissued {marker}",
                    Email = $"sink-reissued-{marker}@example.test",
                    InitialUsername = $"sink-reissued-{marker}",
                }),
            };

            create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);

            var response = await client.SendAsync(create);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            created = body.RootElement.GetProperty("userId").GetGuid();

            var first = await WaitForFilesAsync(1);
            var firstToken = Guid.Parse(ActivationLink().Match(await File.ReadAllTextAsync(first[0])).Groups["id"].Value);

            using var reissue = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{created}/activation-link")
            {
                Content = JsonContent.Create(new { Reason = "The first mail was lost." }),
            };

            reissue.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);

            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(reissue)).StatusCode);

            var second = Assert.Single((await WaitForFilesAsync(2)).Except(first));
            var link = ActivationLink().Match(await File.ReadAllTextAsync(second));

            Assert.True(link.Success, "The reissued message carries no activation link.");

            var (tokenId, secret) = (Guid.Parse(link.Groups["id"].Value), link.Groups["secret"].Value);

            await using var connection = await TestDatabase.OpenAsync();

            await using var open = new NpgsqlCommand(
                """
                SELECT t.id, t.token_hash FROM user_token t
                JOIN user_identity i ON i.id = t.user_identity_id
                WHERE i.user_id = @user AND t.token_type = 'Activation'
                  AND t.used_at IS NULL AND t.invalidated_at IS NULL
                """, connection);

            open.Parameters.AddWithValue("user", created.Value);

            var rows = new List<(Guid Id, string Hash)>();

            await using (var reader = await open.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    rows.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            var only = Assert.Single(rows);

            Assert.Equal(tokenId, only.Id);
            Assert.Equal(Sha256Hex(secret), only.Hash);
            Assert.NotEqual(firstToken, tokenId);
        }
        finally
        {
            if (created is not null)
                await DeleteUserAsync(created.Value);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [GeneratedRegex(@"https://localhost:5173/activate#token=(?<id>[0-9a-f-]{36})\.(?<secret>[A-Za-z0-9_-]+)")]
    private static partial Regex ActivationLink();

    private async Task<string> WaitForFileAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(_directory);

            if (files.Length > 0)
                return Assert.Single(files);

            await Task.Delay(100);
        }

        throw new TimeoutException("No message reached the development mail sink.");
    }

    private async Task<string[]> WaitForFilesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(_directory);

            if (files.Length >= count)
            {
                Assert.Equal(count, files.Length);
                return files;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"{count} message(s) did not reach the development mail sink.");
    }

    private static string Sha256Hex(string secret)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    /// <summary>A user administrator, seeded once and never removed.</summary>
    private static async Task<string> EnsureAdministratorAsync(HttpClient client)
    {
        const string label = "sink-administrator";
        const string username = $"permanent-{label}";

        var userId = new UserId(Administrator.User);

        await using (var existing = CreateContext())
        {
            if (!await existing.Set<User>().AnyAsync(x => x.Id == userId))
                await SeedAdministratorAsync(client, label, username);
        }

        var signIn = await client.PostAsJsonAsync("/api/auth/sign-in", new { Username = username, Password });

        signIn.EnsureSuccessStatusCode();

        return IssuedCarrier.From(signIn);
    }

    /// <summary>
    /// Activated with its own token, seeded directly, so the administrator's own
    /// activation never passes through the sink this test inspects.
    /// </summary>
    private static async Task SeedAdministratorAsync(HttpClient client, string label, string username)
    {
        var now = DateTimeOffset.UtcNow;

        var user = User.CreateHuman(
            new UserId(Administrator.User), "Permanent", "Caller", $"Permanent {label}",
            $"permanent-{label}@example.test", now, User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            new UserIdentityId(Administrator.Identity), user.Id, ActorType.Human, username, now, User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId, identity.Id, TokenType.Activation, material.Hash, now, now.AddHours(72), User.SystemUserId);

        await using (var context = CreateContext())
        {
            context.AddRange(user, identity, token);

            var roleId = await context.Set<Role>()
                .Where(x => x.Code == "user-administrator")
                .Select(x => x.Id)
                .SingleAsync();

            context.Add(
                UserRole.Create(
                    UserRoleId.New(), user.Id, ActorType.Human, roleId,
                    ScopeType.Global, scopeId: null,
                    effectiveFrom: now, effectiveTo: null,
                    assignedAt: now, assignedBy: User.SystemUserId,
                    assignmentReason: "Development mail sink host tests",
                    createdAt: now, createdBy: User.SystemUserId));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        (await client.PostAsJsonAsync("/api/account/activate", new { Token = material.PlainText, NewPassword = Password }))
            .EnsureSuccessStatusCode();
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .AddInterceptors(new ProvenanceStampingInterceptor(new SystemClock(), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    /// <summary>
    /// The created user is the SUBJECT of what USR-C1 wrote, never its actor, so
    /// nothing prevents removing it. Its notification is Sent, so it has a known
    /// fate and may go.
    /// </summary>
    private static async Task DeleteUserAsync(Guid userId)
    {
        await using var connection = await TestDatabase.OpenAsync();

        foreach (var sql in new[]
        {
            """
            UPDATE notification
            SET status = 'NotSent', not_sent_reason = 'Abandoned', closed_at = now()
            WHERE status = 'Pending' AND token_id IN (
                SELECT t.id FROM user_token t
                JOIN user_identity i ON i.id = t.user_identity_id
                WHERE i.user_id = @id)
            """,
            """
            DELETE FROM notification WHERE token_id IN (
                SELECT t.id FROM user_token t
                JOIN user_identity i ON i.id = t.user_identity_id
                WHERE i.user_id = @id)
            """,
            """
            DELETE FROM user_token WHERE user_identity_id IN (
                SELECT id FROM user_identity WHERE user_id = @id)
            """,
            "DELETE FROM user_identity WHERE user_id = @id",
            "DELETE FROM app_user WHERE id = @id",
        })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", userId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
