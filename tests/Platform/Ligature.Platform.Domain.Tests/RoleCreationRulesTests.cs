using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// AUT-C3's input rules in the domain (docs/requirements.md, "AUT-C3
/// CreateRole", RC1 and RC2; RC-A3 and RC-A4).
///
/// THE CODE IS AN IDENTIFIER: refused, never normalised — the username rule,
/// applied to the other identifier this system stores. The NAME and the
/// DESCRIPTION are prose: trimmed, then checked, and stored trimmed, which is
/// the USR-C2 family rather than a third dialect.
/// </summary>
public sealed class RoleCreationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Administrator = new(Guid.Parse("a0000000-0000-4000-8000-000000000001"));

    public static TheoryData<char> EveryWhitespaceCharacter()
    {
        var data = new TheoryData<char>();

        foreach (var character in Enumerable.Range(char.MinValue, char.MaxValue + 1)
                     .Select(x => (char)x)
                     .Where(char.IsWhiteSpace))
        {
            data.Add(character);
        }

        return data;
    }

    // ------------------------------------------------------------ the code

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_refused(string code)
        => Assert.Equal("A role code is required.", Refusal(() => Create(code: code)));

    [Theory]
    [MemberData(nameof(EveryWhitespaceCharacter))]
    public void A_code_with_surrounding_whitespace_is_refused_not_trimmed(char whitespace)
    {
        Assert.Equal(
            "A role code cannot begin or end with whitespace.",
            Refusal(() => Create(code: $"{whitespace}quality-reviewer")));

        Assert.Equal(
            "A role code cannot begin or end with whitespace.",
            Refusal(() => Create(code: $"quality-reviewer{whitespace}")));
    }

    [Fact]
    public void A_code_is_stored_exactly_as_supplied()
    {
        // No grammar, no case folding (RC1): an inner space and mixed case alike.
        Assert.Equal("Quality Reviewer_2", Create(code: "Quality Reviewer_2").Code);
    }

    // ------------------------------------------------------------ the name

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
        => Assert.Equal("A role name is required.", Refusal(() => Create(name: name)));

    [Fact]
    public void A_name_is_stored_trimmed()
        => Assert.Equal("Quality Reviewer", Create(name: "  Quality Reviewer  ").Name);

    [Fact]
    public void A_name_with_a_control_character_is_refused()
        => Assert.Equal(
            "A role name must not contain control characters.",
            Refusal(() => Create(name: "Quality\u0007Reviewer")));

    [Fact]
    public void A_name_of_101_code_points_is_refused_and_100_is_accepted()
    {
        Assert.Equal(
            "A role name must be at most 100 characters.",
            Refusal(() => Create(name: new string('x', 101))));

        Assert.Equal(100, Create(name: new string('x', 100)).Name.Length);
    }

    // ----------------------------------------------------- the description

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_is_stored_as_none(string? description)
        => Assert.Null(Create(description: description).Description);

    [Fact]
    public void A_description_is_stored_trimmed()
        => Assert.Equal("Reviews access.", Create(description: " Reviews access. ").Description);

    [Fact]
    public void A_description_with_a_control_character_is_refused()
        => Assert.Equal(
            "A role description must not contain control characters.",
            Refusal(() => Create(description: "Reviews\u0007access.")));

    [Fact]
    public void A_description_of_101_code_points_is_refused()
        => Assert.Equal(
            "A role description must be at most 100 characters.",
            Refusal(() => Create(description: new string('x', 101))));

    // ---------------------------------------------------------- the create

    [Fact]
    public void A_created_role_is_an_active_tenant_role()
    {
        var role = Create();

        Assert.False(role.IsSystemRole);
        Assert.True(role.IsActive);
        Assert.Equal(Administrator, role.CreatedBy);
    }

    // ------------------------------------------------------------ harness

    private static string Refusal(Action action)
        => Assert.Throws<DomainException>(action).Message;

    private static Role Create(
        string code = "quality-reviewer",
        string name = "Quality Reviewer",
        string? description = "Reviews access.")
        => Role.Create(RoleId.New(), name, code, description, isSystemRole: false, Now, Administrator);
}
