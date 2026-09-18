using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Ligature.Host.Configuration;

namespace Ligature.Host.Tests;

/// <summary>
/// The development mail sink's two locks, proven against a RELEASE build of the
/// host (docs/architecture.md §8) — the configuration the production image
/// publishes. Every other test runs Debug, where the sink exists, so without
/// this suite neither lock would ever be exercised.
///
///   1. The sink is compiled out: the Release persistence assembly carries no
///      type of that name.
///   2. The setting is refused: a Release host given it stops at start-up,
///      naming the setting, instead of ignoring it.
///
/// Slow — it publishes the host — and deliberately so. A lock that is only
/// asserted in the build that has the key proves nothing.
/// </summary>
public sealed class ReleaseBuildTests : IClassFixture<ReleaseBuildTests.ReleaseHost>
{
    private readonly ReleaseHost _release;

    public ReleaseBuildTests(ReleaseHost release) => _release = release;

    [Fact]
    public void The_release_persistence_assembly_contains_no_development_mail_sink()
    {
        var types = TypeNames(Path.Combine(_release.Output, "Ligature.Platform.Persistence.dll"));

        Assert.Contains("GmailTransport", types);
        Assert.DoesNotContain("DevelopmentMailSink", types);
    }

    [Fact]
    public async Task A_release_host_given_the_sink_setting_refuses_to_start_and_names_it()
    {
        var sink = Directory.CreateTempSubdirectory("ligature-release-sink-").FullName;

        try
        {
            var (exitCode, output) = await RunAsync(new Dictionary<string, string>
            {
                [HostConfiguration.PublicBaseUrlSetting] = "https://localhost:5173",
                [HostConfiguration.MailDevSinkDirectorySetting] = sink,
            });

            Assert.NotEqual(0, exitCode);
            Assert.Contains(HostConfiguration.MailDevSinkDirectorySetting, output, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(sink));
        }
        finally
        {
            Directory.Delete(sink, recursive: true);
        }
    }

    private async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyDictionary<string, string> settings)
    {
        var start = new ProcessStartInfo("dotnet", Path.Combine(_release.Output, "Ligature.Host.dll"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _release.Output,
        };

        // Everything the host checks BEFORE mail, so that mail is what it stops
        // on. Nothing is reached: the refusal comes before any connection.
        start.Environment[HostConfiguration.ConnectionSetting] = TestDatabase.ConnectionString;
        start.Environment["LIGATURE_SIGNING_KEY_CURRENT"] = HostFactory.PrimaryKeyId;
        start.Environment["LIGATURE_SIGNING_KEY_V1"] = HostFactory.PrimaryKey;
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        foreach (var (name, value) in settings)
            start.Environment[name] = value;

        using var process = Process.Start(start)!;

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Release host did not stop; it did not refuse the sink setting.");
        }

        return (process.ExitCode, await output + await error);
    }

    private static HashSet<string> TypeNames(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);

        var metadata = pe.GetMetadataReader();

        return metadata.TypeDefinitions
            .Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Publishes the host in Release once for the class.</summary>
    public sealed class ReleaseHost : IAsyncLifetime
    {
        public string Output { get; } = Directory.CreateTempSubdirectory("ligature-release-host-").FullName;

        public async Task InitializeAsync()
        {
            var project = Path.Combine(RepositoryRoot(), "src", "Host", "Ligature.Host", "Ligature.Host.csproj");

            var start = new ProcessStartInfo(
                "dotnet", ["publish", project, "--configuration", "Release", "--output", Output, "--nologo"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(start)!;

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"The Release publish failed:\n{await output}\n{await error}");
        }

        public Task DisposeAsync()
        {
            Directory.Delete(Output, recursive: true);
            return Task.CompletedTask;
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ligature.slnx")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
        }
    }
}
