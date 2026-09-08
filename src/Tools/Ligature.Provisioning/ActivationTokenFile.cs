using System.Text;
using Ligature.Platform.Persistence.Provisioning;

namespace Ligature.Provisioning;

/// <summary>
/// Makes the one-time activation token durable, or fails loudly enough that
/// PRV-C3 rolls back around it.
///
/// This runs as PRV-C3's delivery callback — after the rows are written, before
/// the transaction commits. Everything it can refuse, it refuses in that
/// window, where refusing is free: nothing has been committed, and the operator
/// simply fixes the path and runs again.
/// </summary>
internal static class ActivationTokenFile
{
    /// <summary>
    /// Owner read and write, nothing else. Applied AT CREATION rather than
    /// afterwards, so the file never briefly exists at whatever the umask
    /// would have given it.
    /// </summary>
    private const UnixFileMode OwnerOnly =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static async Task WriteAsync(
        string path,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var full = Path.GetFullPath(path);

        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            throw new ProvisioningException(
                $"The directory '{directory}' does not exist, so the activation "
                + "token cannot be written there. Nothing has been provisioned; "
                + "create the directory and run again.");
        }

        // Checked for the sake of the message; FileMode.CreateNew below is what
        // actually makes the refusal atomic. An existing file is far more
        // likely to be a previous run's token than a mistake worth clobbering,
        // and overwriting one would destroy a credential.
        if (File.Exists(full))
        {
            throw new ProvisioningException(
                $"'{full}' already exists and will not be overwritten. If it is "
                + "left over from a run that did not complete, delete it and "
                + "run again. Nothing has been provisioned.");
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        // Ignored on Windows, where it would throw rather than be a no-op.
        // The repository targets Unix-like hosts today; on Windows the file
        // would inherit directory ACLs instead, which is weaker.
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerOnly;

        await using var stream = new FileStream(full, options);

        // A trailing newline and nothing else. No metadata, so the file can be
        // handed straight to whatever consumes it without anything having to
        // know how to strip a header. The expiry is not secret and is reported
        // on stdout instead.
        await stream.WriteAsync(
            Encoding.UTF8.GetBytes(token + Environment.NewLine),
            cancellationToken);

        await stream.FlushAsync(cancellationToken);

        // THE line that makes the write-ahead ordering mean anything. Without
        // flushing to disk, "delivered" means "sitting in a buffer", and the
        // commit that follows could outlive it.
        //
        // This fsyncs the file. Portable .NET cannot additionally fsync the
        // parent directory entry, so a power loss could in principle lose a
        // newly created file whose contents were synced. That window is far
        // narrower than the one this ordering closes, and is stated rather than
        // pretended away.
        stream.Flush(flushToDisk: true);
    }
}
