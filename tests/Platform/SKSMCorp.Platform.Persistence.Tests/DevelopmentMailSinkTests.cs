using SKSMCorp.Platform.Application.Notifications;
using SKSMCorp.Platform.Persistence.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace SKSMCorp.Platform.Persistence.Tests;

/// <summary>
/// The development mail sink (docs/architecture.md §8, "development mail
/// sink"): a transport that writes each rendered message to a file instead of
/// sending it, so a developer without a Google Workspace account can still follow
/// an activation or reset link.
///
/// These run in Debug, where the sink exists. That it does NOT exist in a
/// Release build is proven separately, against a Release build of the host.
/// </summary>
public sealed class DevelopmentMailSinkTests : IDisposable
{
    private const string Link = "https://localhost:5173/activate#token=0f8e2c1a-0000-4000-8000-000000000001.s3cr3t";

    private readonly string _directory = Directory.CreateTempSubdirectory("sksmcorp-mail-sink-").FullName;

    [Fact]
    public async Task A_message_is_written_to_one_file_with_its_recipient_subject_and_link()
    {
        await SendAsync(Message());

        var file = Assert.Single(Directory.GetFiles(_directory));
        var content = await File.ReadAllTextAsync(file);

        Assert.Contains("To: ada@example.test", content, StringComparison.Ordinal);
        Assert.Contains("Subject: Activate your SKSMCorp account", content, StringComparison.Ordinal);
        Assert.Contains(Link, content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row records where the message went: the sink, and which file. It is
    /// the only evidence a Sent row can point at (N7), and it must not read as
    /// though a mail provider accepted anything.
    /// </summary>
    [Fact]
    public async Task The_reference_names_the_sink_and_the_file()
    {
        var id = await SendAsync(Message());

        var file = Assert.Single(Directory.GetFiles(_directory));

        Assert.Equal($"dev-sink:{Path.GetFileName(file)}", id);
    }

    [Fact]
    public async Task Every_message_gets_its_own_file()
    {
        await SendAsync(Message());
        await SendAsync(Message());
        await SendAsync(Message());

        Assert.Equal(3, Directory.GetFiles(_directory).Length);
    }

    /// <summary>
    /// The file holds a live credential, so it is owner-read/write from the
    /// moment it exists — applied at creation, never afterwards.
    /// </summary>
    [Fact]
    public async Task The_file_is_readable_by_its_owner_only()
    {
        // Asserted unconditionally: the repository targets Unix-like hosts, and
        // a skipped check would pass by asserting nothing.
        await SendAsync(Message());

        var file = Assert.Single(Directory.GetFiles(_directory));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    /// <summary>
    /// The sink never creates its directory. A missing one is a configuration
    /// mistake, and the transport's refusal becomes NotSent/TransportFailed on
    /// the row rather than a link written somewhere nobody looks.
    /// </summary>
    [Fact]
    public async Task A_missing_directory_is_refused_and_nothing_is_created()
    {
        var missing = Path.Combine(_directory, "not-there");

        var sink = new DevelopmentMailSink(missing, NullLogger<DevelopmentMailSink>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sink.SendAsync(Message(), CancellationToken.None));

        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    /// As GmailTransport refuses it: a recipient carrying a control character
    /// would inject a header line into the written message.
    /// </summary>
    [Theory]
    [InlineData("victim@example.test\r\nSubject: Hijacked")]
    [InlineData("victim@example.test\n")]
    [InlineData("victim@example.test\t")]
    public async Task A_recipient_carrying_a_control_character_is_refused_and_nothing_is_written(string recipient)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(Message() with { Recipient = recipient }));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static RenderedMessage Message() =>
        new("ada@example.test", "Activate your SKSMCorp account", $"Hello Ada,\n\nActivate your account: {Link}\n");

    private Task<string?> SendAsync(RenderedMessage message) =>
        new DevelopmentMailSink(_directory, NullLogger<DevelopmentMailSink>.Instance)
            .SendAsync(message, CancellationToken.None);
}
