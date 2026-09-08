namespace Ligature.Provisioning;

/// <summary>
/// The tool's arguments, parsed and validated before anything touches a
/// database.
///
/// None of these are secret — a name, an email address, a username and an
/// output path — so command-line arguments are the right transport. The one
/// secret in this operation travels the other way, out of the tool, and never
/// appears here.
/// </summary>
internal sealed record ProvisioningOptions(
    string FirstName,
    string LastName,
    string DisplayName,
    string Email,
    string Username,
    string ActivationTokenOut)
{
    internal const string ConnectionSetting = "LIGATURE_CONNECTION";

    private const string FirstNameArgument = "--first-name";
    private const string LastNameArgument = "--last-name";
    private const string DisplayNameArgument = "--display-name";
    private const string EmailArgument = "--email";
    private const string UsernameArgument = "--username";
    private const string TokenOutArgument = "--activation-token-out";

    internal static string Usage =>
        $"""
         Provisions a migrated but unprovisioned Ligature database: the platform
         baseline (PRV-C1) and the bootstrap administrator (PRV-C3).

         Usage:
           Ligature.Provisioning
             {FirstNameArgument} <name>
             {LastNameArgument} <name>
             {DisplayNameArgument} <name>
             {EmailArgument} <address>
             {UsernameArgument} <username>
             {TokenOutArgument} <path>

         The target database comes from {ConnectionSetting}. There is no
         default: this tool writes to whatever it is pointed at, so it will not
         guess.

         Apply the schema first with:
           dotnet ef database update --project src/Platform/Ligature.Platform.Persistence

         {TokenOutArgument} receives the administrator's one-time activation
         token. The token is never printed. It cannot be recovered if the file
         is lost, because only its hash is stored — provision a fresh database
         instead.
         """;

    /// <summary>
    /// Returns null and an operator-readable reason rather than throwing.
    /// A mistyped argument is not an exceptional condition; it is the most
    /// likely outcome of running this for the first time.
    /// </summary>
    internal static ProvisioningOptions? Parse(
        IReadOnlyList<string> arguments,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];

            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Expected an option name but found '{name}'.";

                return null;
            }

            if (index + 1 >= arguments.Count)
            {
                error = $"Option '{name}' has no value.";

                return null;
            }

            if (!values.TryAdd(name, arguments[index + 1]))
            {
                error = $"Option '{name}' was given more than once.";

                return null;
            }
        }

        var required = new[]
        {
            FirstNameArgument, LastNameArgument, DisplayNameArgument,
            EmailArgument, UsernameArgument, TokenOutArgument,
        };

        var missing = required
            .Where(x => !values.TryGetValue(x, out var value)
                        || string.IsNullOrWhiteSpace(value))
            .ToList();

        if (missing.Count > 0)
        {
            error = $"Missing required options: {string.Join(", ", missing)}.";

            return null;
        }

        var unknown = values.Keys.Except(required, StringComparer.Ordinal).ToList();

        if (unknown.Count > 0)
        {
            // Refused rather than ignored: a typo'd option that is silently
            // dropped becomes a required one that silently went missing.
            error = $"Unknown options: {string.Join(", ", unknown)}.";

            return null;
        }

        error = null;

        return new ProvisioningOptions(
            values[FirstNameArgument].Trim(),
            values[LastNameArgument].Trim(),
            values[DisplayNameArgument].Trim(),
            values[EmailArgument].Trim(),
            values[UsernameArgument].Trim(),
            values[TokenOutArgument]);
    }

    internal static bool IsHelpRequest(IReadOnlyList<string> arguments)
        => arguments.Count == 0
           || arguments.Any(
               x => x is "--help" or "-h" or "-?" or "/?");
}
