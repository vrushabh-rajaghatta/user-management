using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// USR-Q1 — the read, against real PostgreSQL. What only the database can
/// prove: which rows are users of the list, the exact order, and that offset
/// and limit select a window of that order.
///
/// THE ORDERING TESTS ARE CHOSEN TO FAIL ON THIS DATABASE'S DEFAULT COLLATION.
/// The test database is glibc (en_US.utf8), the deployment is musl, and the
/// contract names neither: it names ICU "unicode". So the seeded names include
/// a pair glibc orders differently from ICU ("de la Cruz" / "Delacroix") and a
/// pair byte order orders differently from ICU ("adam" / "Bob"). A reader that
/// omitted the explicit collation would pass an ordering test built only from
/// names every collation agrees on.
///
/// The database is shared and holds other suites' permanent users, so
/// assertions about seeded rows compare their RELATIVE order, and assertions
/// about the whole set compare against the database's own human rows read in
/// the same (non-parallel) run.
/// </summary>
public sealed class UserListReaderTests
{
    [Fact]
    public async Task Only_human_users_are_read()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var read = await ReadAllAsync(pageSize: 100);

        Assert.DoesNotContain(read, x => x.UserId == User.SystemUserId);
        Assert.Equal(
            (await HumanIdsAsync()).OrderBy(x => x),
            read.Select(x => x.UserId.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task A_row_carries_the_users_id_display_name_and_email()
    {
        await WithSeededAsync(["Row Shape"], async seeded =>
        {
            var row = Assert.Single(await ReadAllAsync(pageSize: 100), x => x.UserId.Value == seeded[0].Id);

            Assert.Equal(seeded[0].DisplayName, row.DisplayName);
            Assert.Equal(seeded[0].Email, row.Email);
        });
    }

    [Fact]
    public async Task A_user_without_an_email_is_read_with_a_null_email()
    {
        await WithSeededAsync(["No Address"], async seeded =>
        {
            var row = Assert.Single(await ReadAllAsync(pageSize: 100), x => x.UserId.Value == seeded[0].Id);

            Assert.Null(row.Email);
        }, withoutEmail: true);
    }

    [Fact]
    public async Task Display_names_are_ordered_under_icu_unicode_not_the_database_default()
    {
        // ICU root order. glibc puts "Delacroix" before "de la Cruz" and "_x"
        // last; byte order puts "Bob" before "adam" and "_x" after both.
        string[] expected = ["_x", "adam", "Bob", "de la Cruz", "Delacroix", "Émile", "Zoë"];

        await WithSeededAsync(expected.Reverse().ToArray(), async seeded =>
        {
            var ids = seeded.Select(x => x.Id).ToHashSet();

            var order = (await ReadAllAsync(pageSize: 100))
                .Where(x => ids.Contains(x.UserId.Value))
                .Select(x => seeded.Single(s => s.Id == x.UserId.Value).Name);

            Assert.Equal(expected, order);
        });
    }

    [Fact]
    public async Task Equal_display_names_are_ordered_by_user_id()
    {
        // PostgreSQL compares uuid by its bytes, which is the order of the
        // canonical lower-case text — NOT System.Guid's CompareTo.
        Guid[] ids =
        [
            Guid.Parse("c0000000-0000-4000-8000-00000000a0b1"),
            Guid.Parse("a0000000-0000-4000-8000-00000000a0b1"),
            Guid.Parse("b0000000-0000-4000-8000-00000000a0b1"),
        ];

        await WithSeededAsync(["Same Name", "Same Name", "Same Name"], async seeded =>
        {
            var order = (await ReadAllAsync(pageSize: 100))
                .Where(x => ids.Contains(x.UserId.Value))
                .Select(x => x.UserId.Value.ToString());

            Assert.Equal(ids.Select(x => x.ToString()).Order(StringComparer.Ordinal), order);
        }, ids);
    }

    [Fact]
    public async Task Offset_and_limit_select_a_window_of_the_order()
    {
        await WithSeededAsync(["Window A", "Window B", "Window C", "Window D", "Window E"], async _ =>
        {
            var all = await ReadAllAsync(pageSize: 100);

            var window = await ReadAsync(offset: 2, limit: 3);

            Assert.Equal(all.Skip(2).Take(3), window);
        });
    }

    [Fact]
    public async Task Pages_of_any_size_neither_repeat_nor_omit_a_row()
    {
        await WithSeededAsync(["Page A", "Page B", "Page C", "Page D", "Page E", "Page F", "Page G"], async _ =>
        {
            var byHundred = await ReadAllAsync(pageSize: 100);
            var byThree = await ReadAllAsync(pageSize: 3);

            Assert.Equal(byHundred, byThree);
            Assert.Equal(byHundred.Count, byHundred.Select(x => x.UserId).Distinct().Count());
        });
    }

    [Fact]
    public async Task A_window_past_the_end_is_empty()
    {
        await TestDatabase.EnsureProvisionedAsync();

        var count = (await HumanIdsAsync()).Count;

        Assert.Empty(await ReadAsync(offset: count, limit: 10));
        Assert.Empty(await ReadAsync(offset: count + 1000, limit: 10));
    }

    [Fact]
    public async Task The_limit_is_honoured()
    {
        await WithSeededAsync(["Limit A", "Limit B", "Limit C"], async _ =>
        {
            Assert.Equal(2, (await ReadAsync(offset: 0, limit: 2)).Count);
        });
    }

    // ----------------------------------------------------------- harness

    private sealed record Seeded(Guid Id, string Name, string DisplayName, string Email);

    /// <summary>
    /// Seeds human users whose display names BEGIN with the given names, so the
    /// suffix that keeps them unique cannot decide their order.
    /// </summary>
    private static async Task WithSeededAsync(
        string[] names,
        Func<IReadOnlyList<Seeded>, Task> body,
        Guid[]? ids = null,
        bool withoutEmail = false)
    {
        await TestDatabase.EnsureProvisionedAsync();

        var marker = Guid.NewGuid().ToString("N")[..10];
        var system = User.SystemUserId.Value;

        var seeded = names
            .Select((name, i) => new Seeded(
                ids?[i] ?? Guid.NewGuid(),
                name,
                $"{name} {marker}",
                $"usr-q1-{marker}-{i}@example.test"))
            .ToList();

        await using (var connection = await TestDatabase.OpenAsync())
        {
            foreach (var user in seeded)
            {
                await using var command = new NpgsqlCommand(
                    """
                    INSERT INTO app_user
                        (id, actor_type, first_name, last_name, display_name, email, status,
                         created_at, created_by, updated_at, updated_by)
                    VALUES
                        (@id, 'Human', 'List', 'Reader', @display, @email, 'Active',
                         now(), @system, now(), @system)
                    """, connection);

                command.Parameters.AddWithValue("id", user.Id);
                command.Parameters.AddWithValue("display", user.DisplayName);
                command.Parameters.AddWithValue("email", withoutEmail ? DBNull.Value : user.Email);
                command.Parameters.AddWithValue("system", system);

                await command.ExecuteNonQueryAsync();
            }
        }

        try
        {
            await body(seeded);
        }
        finally
        {
            await using var connection = await TestDatabase.OpenAsync();
            await using var command = new NpgsqlCommand(
                "DELETE FROM app_user WHERE id = ANY(@ids)", connection);

            command.Parameters.AddWithValue("ids", seeded.Select(x => x.Id).ToArray());

            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<IReadOnlyList<UserListRow>> ReadAsync(int offset, int limit)
    {
        await using var context = CreateContext();

        return await new UserListReader(context).ReadAsync(offset, limit, CancellationToken.None);
    }

    private static async Task<List<UserListRow>> ReadAllAsync(int pageSize)
    {
        var all = new List<UserListRow>();

        for (var offset = 0; ; offset += pageSize)
        {
            var page = await ReadAsync(offset, pageSize);

            all.AddRange(page);

            if (page.Count < pageSize)
                return all;
        }
    }

    private static async Task<List<Guid>> HumanIdsAsync()
    {
        await using var connection = await TestDatabase.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM app_user WHERE actor_type = 'Human'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<Guid>();

        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));

        return ids;
    }

    private static LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(TestDatabase.ConnectionString)
            .Options;

        return new LigatureDbContext(options);
    }
}
