using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Dispatching;
using SKSMCorp.Platform.Application.Roles.Queries.RoleMembers;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Tests.Dispatching;

/// <summary>
/// A query may declare more than one required permission, and ALL of them must
/// hold (docs/requirements.md, RH13; docs/architecture.md §11 as amended).
///
/// WHY A SET RATHER THAN A SECOND CHECK IN THE HANDLER. §11 makes the
/// declaration the single source of truth precisely because "a handler that
/// forgets its authorization check fails silently — it serves the data". A
/// query declaring role.read while its handler also enforced user.read would
/// be that failure with the paperwork filed: the declaration would be
/// verified, and would be a half-truth.
///
/// These tests pin the MECHANISM. Whether AUT-Q4 refuses a particular caller
/// is the read's own test, not this file's.
/// </summary>
public sealed class MultiPermissionQueryAuthorizationTests
{
    // ------------------------------------------------ the classification

    [Fact]
    public void A_classification_can_carry_several_codes_in_declaration_order()
    {
        var authorization = QueryAuthorization.Required("role.read", "user.read");

        Assert.True(authorization.IsRequired);
        Assert.Equal(["role.read", "user.read"], authorization.PermissionCodes);
    }

    /// <summary>
    /// The single-permission factory is unchanged, and answers a one-element
    /// set — so one code and several are the same shape to every consumer.
    /// </summary>
    [Fact]
    public void One_code_is_a_set_of_one()
    {
        var authorization = QueryAuthorization.Required("user.read");

        Assert.Equal(["user.read"], authorization.PermissionCodes);
        Assert.Equal("user.read", authorization.PermissionCode);
    }

    [Fact]
    public void NotRequired_names_no_codes_at_all()
    {
        Assert.False(QueryAuthorization.NotRequired.IsRequired);
        Assert.Empty(QueryAuthorization.NotRequired.PermissionCodes);
        Assert.Throws<InvalidOperationException>(() => QueryAuthorization.NotRequired.PermissionCode);
    }

    /// <summary>
    /// Reading ONE code from a classification that declares several must
    /// throw, not answer the first. A caller that reads one code enforces one
    /// code, and that is the silent half-enforcement this whole design exists
    /// to prevent.
    /// </summary>
    [Fact]
    public void The_singular_code_is_refused_when_several_are_declared()
    {
        var authorization = QueryAuthorization.Required("role.read", "user.read");

        var refusal = Assert.Throws<InvalidOperationException>(() => authorization.PermissionCode);

        Assert.Contains("PermissionCodes", refusal.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------ what is not a declaration

    /// <summary>
    /// RH-A11: an empty set is not a declaration of anything — it is the
    /// "somebody left this blank" state wearing the shape of a positive
    /// declaration, and it must be refused where a blank single code already
    /// is: at the factory, by the person making the mistake.
    /// </summary>
    [Fact]
    public void An_empty_set_is_not_a_declaration()
    {
        Assert.ThrowsAny<ArgumentException>(() => QueryAuthorization.Required(Array.Empty<string>()));
    }

    /// <summary>RH-A11: a blank code ANYWHERE in the set, not merely the first.</summary>
    [Theory]
    [InlineData("", "user.read")]
    [InlineData("role.read", "")]
    [InlineData("role.read", "   ")]
    [InlineData("  ", "user.read")]
    public void A_blank_code_anywhere_in_a_set_is_refused(string first, string second)
    {
        Assert.ThrowsAny<ArgumentException>(() => QueryAuthorization.Required(first, second));
    }

    [Fact]
    public void A_null_set_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => QueryAuthorization.Required((string[])null!));
    }

    // ------------------------------------------------ start-up verification

    /// <summary>
    /// RH-A11, the second layer. The verifier accepts a well-formed set and
    /// still refuses a missing classification.
    ///
    /// THE EMPTY AND BLANK CASES CANNOT BE ASSERTED HERE, and that is the
    /// design rather than a gap: the factories are the only way to obtain a
    /// classification, and they now refuse both — so no query type can present
    /// one to the verifier. Its guards are unreachable by construction,
    /// exactly as the pre-existing blank single-code guard already was, and
    /// they are kept for the same reason §11 wants two layers: the first is
    /// the one that can be bypassed by adding a path, and the second is what
    /// would catch it.
    /// </summary>
    [Fact]
    public void The_verifier_accepts_a_set_and_still_refuses_a_missing_classification()
    {
        Assert.Null(QueryAuthorizationVerification.ProblemWith(
            typeof(DeclaresTwo), QueryAuthorization.Required("role.read", "user.read")));

        Assert.NotNull(QueryAuthorizationVerification.ProblemWith(
            typeof(DeclaresNothing), authorization: null));
    }

    /// <summary>
    /// RH-A12: AUT-Q4 declares both codes, and it is the only query that
    /// declares more than one. A second multi-permission read is a decision,
    /// not a default.
    /// </summary>
    [Fact]
    public void The_role_member_read_declares_both_codes()
    {
        Assert.Equal(["role.read", "user.read"], RoleMembersQuery.Authorization.PermissionCodes);
    }

    private sealed record DeclaresNothing : IQuery<string>;

    private sealed record DeclaresTwo : IQuery<string>;
}
