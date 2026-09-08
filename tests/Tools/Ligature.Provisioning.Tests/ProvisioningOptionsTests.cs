namespace Ligature.Provisioning.Tests;

/// <summary>
/// Pure: no database, no files.
///
/// Argument handling is where an operator's mistake should be caught, because
/// it is the only stage at which stopping costs nothing.
/// </summary>
public sealed class ProvisioningOptionsTests
{
    [Fact]
    public void A_complete_command_line_parses()
    {
        var options = ProvisioningOptions.Parse(Complete(), out var error);

        Assert.Null(error);
        Assert.NotNull(options);

        Assert.Equal("Ada", options!.FirstName);
        Assert.Equal("Lovelace", options.LastName);
        Assert.Equal("Ada Lovelace", options.DisplayName);
        Assert.Equal("ada@example.test", options.Email);
        Assert.Equal("ada.lovelace", options.Username);
        Assert.Equal("/tmp/token", options.ActivationTokenOut);
    }

    [Theory]
    [InlineData("--first-name")]
    [InlineData("--last-name")]
    [InlineData("--display-name")]
    [InlineData("--email")]
    [InlineData("--username")]
    [InlineData("--activation-token-out")]
    public void Every_option_is_required(string omitted)
    {
        var arguments = Without(omitted);

        Assert.Null(ProvisioningOptions.Parse(arguments, out var error));

        Assert.NotNull(error);
        Assert.Contains(omitted, error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Blank is not supplied. An empty --activation-token-out would otherwise
    /// reach the file writer and fail after PRV-C1 had already run.
    /// </summary>
    [Fact]
    public void A_blank_value_counts_as_missing()
    {
        var arguments = Complete();
        arguments[^1] = "   ";

        Assert.Null(ProvisioningOptions.Parse(arguments, out var error));
        Assert.Contains("--activation-token-out", error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A typo'd option that was merely ignored would present as a required
    /// option that mysteriously went missing — or worse, would parse.
    /// </summary>
    [Fact]
    public void An_unknown_option_is_refused()
    {
        var arguments = Complete().Append("--sudo").Append("yes").ToArray();

        Assert.Null(ProvisioningOptions.Parse(arguments, out var error));
        Assert.Contains("--sudo", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_option_is_refused()
    {
        var arguments = Complete().Append("--username").Append("other").ToArray();

        Assert.Null(ProvisioningOptions.Parse(arguments, out var error));
        Assert.Contains("more than once", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dangling_option_is_refused()
    {
        var arguments = Complete().Append("--username").ToArray();

        Assert.Null(ProvisioningOptions.Parse(arguments, out var error));
        Assert.Contains("no value", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stray_positional_argument_is_refused()
    {
        var arguments = new[] { "provision" }.Concat(Complete()).ToArray();

        Assert.Null(ProvisioningOptions.Parse(arguments, out _));
    }

    [Fact]
    public void No_arguments_is_a_help_request()
        => Assert.True(ProvisioningOptions.IsHelpRequest([]));

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_is_recognised(string flag)
        => Assert.True(ProvisioningOptions.IsHelpRequest([flag]));

    /// <summary>
    /// The usage text is where an operator learns that the schema comes first
    /// and that the token is not recoverable. Both facts are load-bearing.
    /// </summary>
    [Fact]
    public void The_usage_text_states_what_the_operator_must_know()
    {
        Assert.Contains("LIGATURE_CONNECTION", ProvisioningOptions.Usage);
        Assert.Contains("dotnet ef database update", ProvisioningOptions.Usage);
        Assert.Contains("never printed", ProvisioningOptions.Usage);
    }

    private static string[] Complete() =>
    [
        "--first-name", "Ada",
        "--last-name", "Lovelace",
        "--display-name", "Ada Lovelace",
        "--email", "ada@example.test",
        "--username", "ada.lovelace",
        "--activation-token-out", "/tmp/token",
    ];

    private static string[] Without(string option)
    {
        var arguments = Complete();

        var index = Array.IndexOf(arguments, option);

        return arguments.Take(index)
            .Concat(arguments.Skip(index + 2))
            .ToArray();
    }
}
