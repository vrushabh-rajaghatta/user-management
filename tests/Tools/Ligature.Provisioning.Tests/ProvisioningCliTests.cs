namespace Ligature.Provisioning.Tests;

/// <summary>
/// The tool end to end: a real command line, a real migrated-but-empty
/// database, a real token file.
///
/// The property under most of these is that a failure must leave the tenant
/// PROVISIONABLE. PRV-C3 can only ever create one administrator, so any path
/// that commits one without a durable activation token produces a database
/// nobody can log into and nothing can repair.
/// </summary>
public sealed class ProvisioningCliTests : IDisposable
{
    private readonly string? _originalConnection =
        Environment.GetEnvironmentVariable("LIGATURE_CONNECTION");

    private readonly string _workspace =
        Directory.CreateTempSubdirectory("ligature-provisioning-tests").FullName;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            "LIGATURE_CONNECTION", _originalConnection);

        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    // --------------------------------------------------------- the journey

    [Fact]
    public async Task It_provisions_a_migrated_but_empty_database()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        var tokenPath = Path.Combine(_workspace, "bootstrap.token");

        var run = await RunAsync(database, tokenPath);

        Assert.Equal(Program.Success, run.ExitCode);

        Assert.True(File.Exists(tokenPath), "The activation token file is missing.");

        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();

        Assert.False(string.IsNullOrWhiteSpace(token));

        // THE disclosure test. The token is a live credential; the operator is
        // told where it is, never what it is.
        Assert.DoesNotContain(token, run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(token, run.Error, StringComparison.Ordinal);

        Assert.Contains("Provisioning completed.", run.Output);
        Assert.Contains("ada.lovelace", run.Output);
        Assert.Contains(tokenPath, run.Output);

        // Both provisioners ran.
        Assert.Equal(
            1,
            await database.CountAsync(
                "SELECT count(*) FROM app_user WHERE actor_type = 'System'"));

        Assert.Equal(
            1,
            await database.CountAsync(
                "SELECT count(*) FROM app_user WHERE actor_type = 'Human'"));

        Assert.True(
            await database.CountAsync("SELECT count(*) FROM permission") > 0);

        // Inv. 15 — the administrator has no credential until they activate.
        Assert.Equal(
            0, await database.CountAsync("SELECT count(*) FROM credential"));
    }

    /// <summary>
    /// Owner read and write only. The file holds a credential, and it sits
    /// wherever the operator pointed — quite possibly a shared machine.
    /// </summary>
    [Fact]
    public async Task The_token_file_is_readable_only_by_its_owner()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        var tokenPath = Path.Combine(_workspace, "bootstrap.token");

        await RunAsync(database, tokenPath);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(tokenPath));
    }

    /// <summary>
    /// Running twice is the design, not a mistake: both provisioners are
    /// sentinel-guarded. The second run must report that and must NOT touch or
    /// regenerate the token file — rewriting it would replace a token the
    /// operator may already have delivered with one that does not work.
    /// </summary>
    [Fact]
    public async Task A_second_run_changes_nothing_and_leaves_the_token_file_alone()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        var tokenPath = Path.Combine(_workspace, "bootstrap.token");

        await RunAsync(database, tokenPath);

        var original = await File.ReadAllTextAsync(tokenPath);
        var originalWrite = File.GetLastWriteTimeUtc(tokenPath);

        var second = await RunAsync(database, tokenPath);

        // Idempotent is SUCCESS.
        Assert.Equal(Program.Success, second.ExitCode);
        Assert.Contains("already complete", second.Output);

        Assert.Equal(original, await File.ReadAllTextAsync(tokenPath));
        Assert.Equal(originalWrite, File.GetLastWriteTimeUtc(tokenPath));

        Assert.Equal(
            1,
            await database.CountAsync(
                "SELECT count(*) FROM app_user WHERE actor_type = 'Human'"));
    }

    // ------------------------------------------------- refusing to proceed

    /// <summary>
    /// Seeds only. An unmigrated database must be named as such, rather than
    /// failing somewhere inside a seed with a missing-table error that reads
    /// like a provisioning bug.
    /// </summary>
    [Fact]
    public async Task An_unmigrated_database_is_refused_with_the_remedy()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: false);

        var run = await RunAsync(database, Path.Combine(_workspace, "t.token"));

        Assert.Equal(Program.ProvisioningFailed, run.ExitCode);
        Assert.Contains("pending migration", run.Error);
        Assert.Contains("dotnet ef database update", run.Error);

        Assert.False(File.Exists(Path.Combine(_workspace, "t.token")));
    }

    /// <summary>
    /// THE safety test. An existing token file must not be overwritten — and
    /// crucially, nothing may be committed when it refuses. The database has to
    /// remain provisionable after the operator clears the file.
    /// </summary>
    /// <summary>
    /// The sibling of the migrations gate. The audit schema is deployed by a
    /// different tool under a different credential, and a seed run against a
    /// database where it is absent would fail deep inside with a missing-table
    /// error that says nothing about which step was skipped. Refused up front,
    /// with the remedy, before anything is written.
    /// </summary>
    [Fact]
    public async Task A_database_without_the_audit_schema_is_refused_with_the_remedy()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(
            migrated: true, auditDeployed: false);

        var run = await RunAsync(database, Path.Combine(_workspace, "t.token"));

        Assert.Equal(Program.ProvisioningFailed, run.ExitCode);
        Assert.Contains("Audit schema is not deployed", run.Error);
        Assert.Contains("Ligature.AuditSchema", run.Error);

        Assert.False(File.Exists(Path.Combine(_workspace, "t.token")));
        Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM app_user"));
    }

    [Fact]
    public async Task An_existing_token_file_is_refused_and_nothing_is_committed()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        var tokenPath = Path.Combine(_workspace, "occupied.token");

        await File.WriteAllTextAsync(tokenPath, "something already here");

        var run = await RunAsync(database, tokenPath);

        Assert.Equal(Program.ProvisioningFailed, run.ExitCode);
        Assert.Contains("already exists", run.Error);

        // Untouched.
        Assert.Equal(
            "something already here", await File.ReadAllTextAsync(tokenPath));

        // And PRV-C3 rolled back: no administrator, so a retry can still work.
        Assert.Equal(
            0,
            await database.CountAsync(
                "SELECT count(*) FROM app_user WHERE actor_type = 'Human'"));

        Assert.Equal(
            0, await database.CountAsync("SELECT count(*) FROM user_token"));

        // The retry, after clearing the obstruction, succeeds.
        File.Delete(tokenPath);

        Assert.Equal(Program.Success, (await RunAsync(database, tokenPath)).ExitCode);
    }

    /// <summary>
    /// An unwritable destination is the other way delivery fails, and it must
    /// roll back identically.
    /// </summary>
    [Fact]
    public async Task An_impossible_token_path_commits_no_administrator()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        var run = await RunAsync(
            database,
            Path.Combine(_workspace, "no", "such", "directory", "t.token"));

        Assert.Equal(Program.ProvisioningFailed, run.ExitCode);
        Assert.Contains("does not exist", run.Error);

        Assert.Equal(
            0,
            await database.CountAsync(
                "SELECT count(*) FROM app_user WHERE actor_type = 'Human'"));

        // PRV-C1 is idempotent, so its rows surviving is correct and harmless —
        // what must not survive is a half-made administrator.
        Assert.Equal(
            0, await database.CountAsync("SELECT count(*) FROM user_identity"));
    }

    [Fact]
    public async Task A_missing_connection_string_is_refused_before_anything_runs()
    {
        Environment.SetEnvironmentVariable("LIGATURE_CONNECTION", null);

        var run = await InvokeAsync(
            Arguments(Path.Combine(_workspace, "t.token")));

        Assert.Equal(Program.UsageError, run.ExitCode);
        Assert.Contains("LIGATURE_CONNECTION", run.Error);
        Assert.Contains("will not guess", run.Error);
    }

    [Fact]
    public async Task A_bad_command_line_is_refused_with_usage()
    {
        var run = await InvokeAsync(["--first-name", "Ada"]);

        Assert.Equal(Program.UsageError, run.ExitCode);
        Assert.Contains("Missing required options", run.Error);
        Assert.Contains("dotnet ef database update", run.Error);
    }

    [Fact]
    public async Task Help_is_not_an_error()
    {
        var run = await InvokeAsync(["--help"]);

        Assert.Equal(Program.Success, run.ExitCode);
        Assert.Contains("LIGATURE_CONNECTION", run.Output);
    }

    // ------------------------------------------------------------ harness

    private sealed record Run(int ExitCode, string Output, string Error);

    private static string[] Arguments(string tokenPath) =>
    [
        "--first-name", "Ada",
        "--last-name", "Lovelace",
        "--display-name", "Ada Lovelace",
        "--email", "ada@example.test",
        "--username", "ada.lovelace",
        "--activation-token-out", tokenPath,
    ];

    /// <summary>
    /// The defect this asserts against was real, and its failure MODE was
    /// worse than its cause.
    ///
    /// Provisioning hit a missing privilege and raised a raw
    /// PostgresException. Only ProvisioningException was caught, so it escaped
    /// Main as an unhandled exception: the operator got a stack trace, and the
    /// container stayed up. bootstrap.sh never got its shell back, so a plain
    /// error looked like a hang.
    ///
    /// The trigger below reproduces the SHAPE of that failure rather than its
    /// cause — an unanticipated database error raised from inside the audit
    /// write — because the fix must hold for every such error, not just for
    /// the one privilege that was missing.
    ///
    /// That this test RETURNS at all is half of what it proves.
    /// </summary>
    [Fact]
    public async Task An_unanticipated_database_failure_returns_a_code_rather_than_escaping()
    {
        await using var database = await ProvisioningDatabase.CreateAsync(migrated: true);

        await database.ExecuteAsync(
            """
            CREATE FUNCTION audit.refuse_for_test() RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'audit write refused by test';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER refuse_writes_for_test
                BEFORE INSERT ON audit.audit_record
                FOR EACH ROW EXECUTE FUNCTION audit.refuse_for_test();
            """);

        var tokenPath = Path.Combine(_workspace, "unanticipated.token");

        var run = await RunAsync(database, tokenPath);

        Assert.Equal(Program.ProvisioningFailed, run.ExitCode);

        // Says what happened to the database, because the operator's next
        // question is whether it is safe to re-run.
        Assert.Contains("rolled back", run.Error);

        // And nothing was committed, including the token.
        Assert.False(File.Exists(tokenPath));
        Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM app_user"));
        Assert.Equal(0, await database.CountAsync(
            "SELECT count(*) FROM audit.audit_retention_policy"));
    }

    private static async Task<Run> RunAsync(
        ProvisioningDatabase database, string tokenPath)
    {
        Environment.SetEnvironmentVariable(
            "LIGATURE_CONNECTION", database.ConnectionString);

        return await InvokeAsync(Arguments(tokenPath));
    }

    private static async Task<Run> InvokeAsync(string[] arguments)
    {
        await using var output = new StringWriter();
        await using var error = new StringWriter();

        var exitCode = await Program.RunAsync(
            arguments, output, error, CancellationToken.None);

        return new Run(exitCode, output.ToString(), error.ToString());
    }
}
