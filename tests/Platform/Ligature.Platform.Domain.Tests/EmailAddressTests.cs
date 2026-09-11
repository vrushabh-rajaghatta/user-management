using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// The first tests this value object has ever had, which is not incidental —
/// the absence is why a control-character address went unnoticed until the
/// notification work put one into a mail header.
///
/// So this covers the whole contract rather than only the defect. An untested
/// rule is a rule someone will "simplify" later, and the existing checks encode
/// real decisions: 64 for the local part, 254 overall, absolutely one @, a
/// dotted domain, trimming, and case-insensitive equality because the same
/// mailbox reached by different capitalisation is the same mailbox.
/// </summary>
public sealed class EmailAddressTests
{
    // --------------------------------------------------------------- shape

    [Fact]
    public void An_ordinary_address_is_accepted_and_preserved()
    {
        var email = EmailAddress.Create("John.Smith@example.com");

        // Preserved, not normalised: the local part is case-sensitive by RFC,
        // and rewriting what somebody typed would make the stored value differ
        // from the address they expect mail at.
        Assert.Equal("John.Smith@example.com", email.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_an_address(string? raw)
        => Assert.Null(EmailAddress.TryCreate(raw));

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        // Long-standing behaviour, and the reason the control-character check
        // runs after the trim rather than before it.
        Assert.Equal("a@b.com", EmailAddress.Create("  a@b.com  ").Value);
    }

    [Theory]
    [InlineData("no-at-sign.example.com")]      // no @ at all
    [InlineData("@example.com")]                 // nothing before it
    [InlineData("a@")]                           // nothing after it
    [InlineData("two@at@example.com")]           // more than one
    [InlineData("a@nodot")]                      // domain without a dot
    public void A_malformed_address_is_refused(string raw)
        => Assert.Null(EmailAddress.TryCreate(raw));

    [Fact]
    public void A_local_part_longer_than_sixty_four_characters_is_refused()
    {
        Assert.Null(EmailAddress.TryCreate(new string('a', 65) + "@example.com"));

        Assert.NotNull(EmailAddress.TryCreate(new string('a', 64) + "@example.com"));
    }

    [Fact]
    public void An_address_longer_than_two_hundred_and_fifty_four_characters_is_refused()
    {
        var domain = "@" + new string('d', 240) + ".com";

        Assert.Null(EmailAddress.TryCreate(new string('a', 20) + domain));

        Assert.NotNull(EmailAddress.TryCreate(new string('a', 8) + domain));
    }

    [Fact]
    public void Create_throws_where_TryCreate_returns_null()
    {
        var failure = Assert.Throws<DomainException>(
            () => EmailAddress.Create("not-an-address"));

        Assert.Contains("invalid format", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ equality

    [Fact]
    public void The_same_mailbox_in_different_case_is_the_same_address()
    {
        var lower = EmailAddress.Create("john@example.com");
        var upper = EmailAddress.Create("JOHN@EXAMPLE.COM");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());

        // Asserted as it actually behaves, not as it ought to. EmailAddress
        // overrides Equals and GetHashCode but does NOT overload ==, so the
        // operator is reference equality while Equals is value equality and the
        // two disagree. StronglyTypedId overloads both; this type is
        // inconsistent with it.
        //
        // Left alone deliberately — changing == is a behavioural change outside
        // this story. Pinned so the inconsistency is visible rather than
        // lurking, and so that adding the operator later fails here and forces
        // the decision to be made consciously.
        Assert.False(lower == upper);
    }

    [Fact]
    public void Different_mailboxes_are_not_equal()
        => Assert.NotEqual(
            EmailAddress.Create("john@example.com"),
            EmailAddress.Create("jane@example.com"));

    // -------------------------------------------------- control characters

    /// <summary>
    /// The security rule. Every boundary of both ranges char.IsControl covers,
    /// because an off-by-one here is a header-injection vector rather than a
    /// cosmetic defect.
    /// </summary>
    [Theory]
    [InlineData('\u0000')]   // NUL — the low boundary of C0
    [InlineData('\u0001')]   // SOH — first non-whitespace control
    [InlineData('\u000D')]   // CR  — the injection vector itself
    [InlineData('\u000A')]   // LF  — and its partner
    [InlineData('\u001F')]   // US  — the high boundary of C0
    [InlineData('\u007F')]   // DEL
    [InlineData('\u0080')]   // the low boundary of C1
    [InlineData('\u009F')]   // the high boundary of C1
    public void A_control_character_in_the_local_part_is_refused(char control)
        => Assert.Null(EmailAddress.TryCreate($"jo{control}hn@example.com"));

    [Theory]
    [InlineData('\u0000')]
    [InlineData('\u0001')]
    [InlineData('\u000D')]
    [InlineData('\u000A')]
    [InlineData('\u001F')]
    [InlineData('\u007F')]
    [InlineData('\u0080')]
    [InlineData('\u009F')]
    public void A_control_character_in_the_domain_is_refused(char control)
        => Assert.Null(EmailAddress.TryCreate($"john@exa{control}mple.com"));

    [Fact]
    public void The_header_injection_payload_is_refused()
    {
        // Verbatim the shape that reaches a mail header. It passed every check
        // this type had before: one @, a dotted domain, inside the length
        // bounds — and the transport's CRLF normalisation would have turned it
        // into a well-formed injected header rather than corrupting it.
        Assert.Null(EmailAddress.TryCreate("victim@example.com\r\nSubject: Hijacked"));
    }

    [Theory]
    [InlineData('\u0020')]   // space — the character just past C0
    [InlineData('\u00A0')]   // NBSP  — the character just past C1
    public void A_character_adjacent_to_the_control_ranges_is_not_treated_as_control(
        char boundary)
    {
        // Guards the range ends from being widened by a careless "tidy-up".
        // Neither is a control character, and neither should be refused for
        // being one — the space here is interior, so trimming cannot mask it.
        Assert.NotNull(EmailAddress.TryCreate($"jo{boundary}hn@example.com"));
    }

    /// <summary>
    /// The placement decision, stated as behaviour: a whitespace control at the
    /// edge is trimmed away and the address stands, while the same character in
    /// the middle is refused. Checking before the trim would reject both and
    /// silently narrow a contract that has always accepted padded input.
    /// </summary>
    [Fact]
    public void A_whitespace_control_is_trimmed_at_the_edge_and_refused_in_the_middle()
    {
        Assert.Equal("a@b.com", EmailAddress.Create("\ta@b.com\r\n").Value);

        Assert.Null(EmailAddress.TryCreate("a\tb@c.com"));
    }
}
