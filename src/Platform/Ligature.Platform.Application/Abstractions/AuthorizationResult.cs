namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// The outcome of an authorisation check, and — when it succeeded — the
/// assignment selected to represent that authority.
///
/// It replaces a bare bool. "IsAllowed = true" cannot answer the question an
/// inspector asks years later, which is not whether the actor was permitted
/// but under what authority they acted.
///
/// IMPORTANT — what the selection does and does not mean. Several assignments
/// may legitimately authorise the same act; a user holding two roles that both
/// carry a permission is an ordinary configuration, not a conflict. This type
/// reports exactly ONE of them, chosen deterministically, and that choice
/// changes only what is RECORDED. It does not change who is authorised: the
/// decision is still "at least one eligible assignment exists", exactly as
/// before.
///
/// The choice is deterministic for a given authorisation state, not stable
/// across changes to it: an assignment selected today can be revoked
/// tomorrow. Recording the AssignmentId is what makes the past reconstructable
/// regardless.
/// </summary>
public sealed record AuthorizationResult
{
    private AuthorizationResult(
        bool isAllowed,
        AuthorizingAssignment? authority)
    {
        IsAllowed = isAllowed;
        Authority = authority;
    }

    public bool IsAllowed { get; }

    /// <summary>
    /// Non-null exactly when <see cref="IsAllowed"/> is true. A permitted act
    /// always has a deciding assignment, because permission is only ever
    /// reached through one.
    /// </summary>
    public AuthorizingAssignment? Authority { get; }

    public static AuthorizationResult Denied { get; } = new(false, null);

    public static AuthorizationResult Allowed(AuthorizingAssignment authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        return new AuthorizationResult(true, authority);
    }
}
