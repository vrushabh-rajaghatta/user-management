using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Domain.Tests;

/// <summary>
/// The local username rule (docs/requirements.md, "Local usernames refuse
/// surrounding whitespace", WS1, WS2, WS9; UW-1 and UW-2).
///
/// Whitespace is exactly .NET's char.IsWhiteSpace — the set string.Trim()
/// removes — and a username is valid on this rule exactly when Trim() would
/// leave it unchanged. It is refused, never trimmed. Invisible format
/// characters are not whitespace, and this rule deliberately accepts them.
/// </summary>
public sealed class UsernameBoundaryTests
{
    private const string Refusal = "A username cannot begin or end with whitespace.";

    private const string Blank = "Username cannot be empty.";

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    /// <summary>WS1, verbatim: the 25 characters the contract and the CHECK name.</summary>
    internal static readonly char[] Whitespace =
    [
        '\u0009', '\u000A', '\u000B', '\u000C', '\u000D',
        '\u0020', '\u0085', '\u00A0', '\u1680',
        '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
        '\u2006', '\u2007', '\u2008', '\u2009', '\u200A',
        '\u2028', '\u2029', '\u202F', '\u205F', '\u3000',
    ];

    public static TheoryData<char> EveryWhitespaceCharacter()
    {
        var data = new TheoryData<char>();

        foreach (var character in Whitespace)
            data.Add(character);

        return data;
    }

    // ---------------------------------------------------------------- UW-1

    [Fact]
    public void The_whitespace_set_is_exactly_the_25_the_contract_names()
    {
        var actual = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(x => (char)x)
            .Where(char.IsWhiteSpace)
            .ToArray();

        Assert.Equal(Whitespace, actual);
    }

    // ---------------------------------------------------------------- UW-2

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void CreateLocal_refuses_it_at_either_end(char whitespace)
    {
        AssertRefused(() => Create($"{whitespace}ada"));
        AssertRefused(() => Create($"ada{whitespace}"));
    }

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void CreateLocal_accepts_it_inside(char whitespace)
    {
        var username = $"a{whitespace}da";

        Assert.Equal(username, Create(username).Username);
    }

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void ChangeUsername_refuses_it_at_either_end_and_keeps_the_old_username(char whitespace)
    {
        var identity = Create("ada");

        AssertRefused(() => identity.ChangeUsername($"{whitespace}grace"));
        AssertRefused(() => identity.ChangeUsername($"grace{whitespace}"));

        Assert.Equal("ada", identity.Username);
    }

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void ChangeUsername_accepts_it_inside(char whitespace)
    {
        var identity = Create("ada");

        Assert.True(identity.ChangeUsername($"gr{whitespace}ace"));
        Assert.Equal($"gr{whitespace}ace", identity.Username);
    }

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void The_rule_itself_refuses_it_at_either_end(char whitespace)
    {
        AssertRefused(() => UserIdentity.ValidateUsernameBoundary($"{whitespace}ada"));
        AssertRefused(() => UserIdentity.ValidateUsernameBoundary($"ada{whitespace}"));

        UserIdentity.ValidateUsernameBoundary($"a{whitespace}da");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_blank_username_keeps_its_own_message(string username)
    {
        Assert.Equal(Blank, Assert.Throws<DomainException>(() => UserIdentity.ValidateUsernameBoundary(username)).Message);
        Assert.Equal(Blank, Assert.Throws<DomainException>(() => Create(username)).Message);
        Assert.Equal(Blank, Assert.Throws<DomainException>(() => Create("ada").ChangeUsername(username)).Message);
    }

    /// <summary>WS9: format characters are not whitespace, so this rule accepts them.</summary>
    [Theory]
    [InlineData("\u200Bada")]
    [InlineData("ada\u200B")]
    [InlineData("\uFEFFada")]
    [InlineData("ada\uFEFF")]
    public void Invisible_format_characters_are_not_this_rule(string username)
    {
        UserIdentity.ValidateUsernameBoundary(username);

        Assert.Equal(username, Create(username).Username);
        Assert.True(Create("ada").ChangeUsername(username));
    }

    [Fact]
    public void An_ordinary_username_is_accepted_everywhere()
    {
        UserIdentity.ValidateUsernameBoundary("ada.lovelace");

        Assert.Equal("ada.lovelace", Create("ada.lovelace").Username);
        Assert.True(Create("ada").ChangeUsername("grace.hopper"));
    }

    // ------------------------------------------------------------ harness

    private static void AssertRefused(Action action)
        => Assert.Equal(Refusal, Assert.Throws<DomainException>(action).Message);

    private static UserIdentity Create(string username)
        => UserIdentity.CreateLocal(
            UserIdentityId.New(), UserId.New(), ActorType.Human, username, Now, Administrator);
}
