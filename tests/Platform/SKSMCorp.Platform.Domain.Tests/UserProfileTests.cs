using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Domain.Tests;

/// <summary>
/// USR-C2's name rules, owned by the domain (docs/requirements.md, "USR-C2 —
/// Update User Profile", G3 as confirmed): TRIM, then VALIDATE, then STORE the
/// trimmed value. Required and non-blank; no control characters
/// (char.IsControl, as EmailAddress); at most 100 Unicode CODE POINTS — not
/// UTF-16 units, which is what string.Length counts.
/// </summary>
public sealed class UserProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ normalisation

    [Fact]
    public void Surrounding_whitespace_is_removed_and_interior_whitespace_kept()
    {
        var user = Human();

        Assert.True(user.UpdateProfile("  Ada ", "\tKing  Lovelace\u00A0", " Ada  Lovelace "));

        Assert.Equal("Ada", user.FirstName);
        Assert.Equal("King  Lovelace", user.LastName);
        Assert.Equal("Ada  Lovelace", user.DisplayName);
    }

    /// <summary>P-A4 — a value differing only by surrounding whitespace is no change.</summary>
    [Fact]
    public void A_value_differing_only_by_surrounding_whitespace_is_no_change()
    {
        var user = Human();

        Assert.False(user.UpdateProfile(" John ", "Leaver ", "  John Leaver"));
    }

    [Fact]
    public void Identical_values_are_no_change()
        => Assert.False(Human().UpdateProfile("John", "Leaver", "John Leaver"));

    // ------------------------------------------------------------ required

    [Theory]
    [InlineData("", "Leaver", "John Leaver", "First name is required.")]
    [InlineData("   ", "Leaver", "John Leaver", "First name is required.")]
    [InlineData("John", "\u00A0 ", "John Leaver", "Last name is required.")]
    [InlineData("John", "Leaver", "", "Display name is required.")]
    [InlineData("John", "Leaver", " \t ", "Display name is required.")]
    public void A_blank_name_is_refused_and_nothing_changes(string first, string last, string display, string message)
        => AssertRefused(first, last, display, message);

    // ------------------------------------------------------------ control characters

    [Theory]
    [InlineData("Jo\thn", "Leaver", "John Leaver", "First name must not contain control characters.")]
    [InlineData("John", "Lea\nver", "John Leaver", "Last name must not contain control characters.")]
    [InlineData("John", "Leaver", "John\u0000Leaver", "Display name must not contain control characters.")]
    [InlineData("John", "Leaver", "John\u009FLeaver", "Display name must not contain control characters.")]
    [InlineData("John", "Leaver", "John\u007FLeaver", "Display name must not contain control characters.")]
    public void A_control_character_is_refused_and_nothing_changes(string first, string last, string display, string message)
        => AssertRefused(first, last, display, message);

    // ------------------------------------------------------------ length in code points

    [Fact]
    public void Exactly_100_code_points_is_accepted_even_when_net_counts_more_units()
    {
        var user = Human();

        // 100 astral code points: string.Length is 200.
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", 100));

        Assert.Equal(200, astral.Length);
        Assert.True(user.UpdateProfile(new string('a', 100), "Leaver", astral));
        Assert.Equal(astral, user.DisplayName);
    }

    [Theory]
    [InlineData(101, false, "First name must be at most 100 characters.")]
    [InlineData(101, true, "First name must be at most 100 characters.")]
    public void One_hundred_and_one_code_points_is_refused(int codePoints, bool astral, string message)
    {
        var value = astral
            ? string.Concat(Enumerable.Repeat("\U0001F600", codePoints))
            : new string('a', codePoints);

        AssertRefused(value, "Leaver", "John Leaver", message);
    }

    /// <summary>The limit applies AFTER normalisation: surrounding whitespace does not count.</summary>
    [Fact]
    public void Surrounding_whitespace_does_not_count_towards_the_limit()
    {
        var user = Human();

        Assert.True(user.UpdateProfile("  " + new string('a', 100) + "  ", "Leaver", "John Leaver"));
        Assert.Equal(100, user.FirstName!.Length);
    }

    // ------------------------------------------------------------ helpers

    private static void AssertRefused(string first, string last, string display, string message)
    {
        var user = Human();

        var refusal = Assert.Throws<DomainException>(() => user.UpdateProfile(first, last, display));

        Assert.Equal(message, refusal.Message);
        Assert.Equal(("John", "Leaver", "John Leaver"), (user.FirstName, user.LastName, user.DisplayName));
    }

    private static User Human()
        => User.CreateHuman(
            new UserId(Guid.NewGuid()), "John", "Leaver", "John Leaver", "john.leaver@example.test",
            Now.AddYears(-1), User.SystemUserId);
}
