namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// A query's authorization classification (docs/architecture.md §11).
///
/// TWO EXPLICIT STATES, NEVER ONE NULLABLE VALUE. Both are positive
/// declarations. A single nullable member — where null is taken to mean "no
/// authorization required" — is forbidden by the contract, because it would
/// make the most consequential state in the system the one you get by not
/// typing anything: "deliberately open" and "somebody left this blank" would
/// become indistinguishable.
///
/// That is why reading the permission code of a NotRequired classification
/// throws rather than returning null. There is no quiet answer to a question
/// that should not have been asked.
/// </summary>
public sealed record QueryAuthorization
{
    private readonly IReadOnlyList<string> _permissionCodes;

    // Private, so the ONLY ways to obtain a classification are the factories
    // below. Any non-null instance is therefore one of the two legal states by
    // construction; there is no third state to reach.
    private QueryAuthorization(bool isRequired, IReadOnlyList<string> permissionCodes)
    {
        IsRequired = isRequired;
        _permissionCodes = permissionCodes;
    }

    /// <summary>The query is deliberately open. A declaration, not an absence.</summary>
    public static QueryAuthorization NotRequired { get; } = new(false, []);

    /// <summary>
    /// The query requires this permission. A blank code is not a declaration
    /// and is refused here rather than at start-up, so the mistake is found by
    /// the person making it.
    /// </summary>
    public static QueryAuthorization Required(string permissionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionCode);

        return new QueryAuthorization(true, [permissionCode]);
    }

    /// <summary>
    /// The query requires EVERY one of these permissions (RH13). AND, never
    /// OR: a caller holding some of them is refused exactly as one holding
    /// none is.
    ///
    /// A set rather than a second undeclared check inside the handler, because
    /// the declaration is the single source of truth §11 made it. A handler
    /// that enforces a permission its query does not declare is the silent
    /// failure the whole contract exists to prevent.
    /// </summary>
    public static QueryAuthorization Required(params string[] permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);

        // An EMPTY SET is the "somebody left this blank" state wearing the
        // shape of a positive declaration, and it is refused for the same
        // reason a nullable permission code is: it would make the most
        // consequential state in the system the one you get by not typing
        // anything.
        if (permissionCodes.Length == 0)
        {
            throw new ArgumentException(
                "A required classification must name at least one permission. "
                + "Declare NotRequired for a query that is deliberately open.",
                nameof(permissionCodes));
        }

        // ANYWHERE in the set, not merely the first: a blank code in any
        // position is a permission nobody can hold, and enforcing it would
        // refuse every caller for a reason no one declared.
        foreach (var code in permissionCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException(
                    "A required classification cannot name a blank permission.",
                    nameof(permissionCodes));
            }
        }

        return new QueryAuthorization(true, [.. permissionCodes]);
    }

    public bool IsRequired { get; }

    /// <summary>
    /// Every code the query requires, in declaration order. Empty for
    /// NotRequired; one element for the single-permission case, which is most
    /// of them.
    /// </summary>
    public IReadOnlyList<string> PermissionCodes => _permissionCodes;

    /// <summary>
    /// The one code, for the single-permission case. Throws when the
    /// classification is NotRequired, and throws for a multi-permission
    /// classification rather than silently answering with the first of
    /// several — a caller that reads one code would enforce one code.
    /// </summary>
    public string PermissionCode
        => (IsRequired, _permissionCodes.Count) switch
        {
            (true, 1) => _permissionCodes[0],
            (true, _) => throw new InvalidOperationException(
                $"This query declares {_permissionCodes.Count} permissions, so it has no single "
                + "permission code. Read PermissionCodes and enforce every one."),
            _ => throw new InvalidOperationException(
                "This query declares NotRequired, so it has no permission code. "
                + "Check IsRequired before reading one."),
        };

    /// <summary>
    /// Written by hand because the compiler's version prints every public
    /// property — and PermissionCode deliberately throws for NotRequired, which
    /// would make ToString throw in a debugger, a log line or an assertion
    /// message.
    /// </summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(IsRequired
            ? $"Required = {string.Join(", ", _permissionCodes)}"
            : "NotRequired");

        return true;
    }
}

/// <summary>
/// Declares a query's authorization classification, on the query type itself.
///
/// STATIC ABSTRACT, and the pattern note in §11 explains why: registration
/// holds the query TYPE and not an instance, so an instance member cannot be
/// read there, and a static member or attribute read by reflection would
/// reintroduce the scanning this architecture bans while leaving the compiler
/// unable to force one to exist.
///
/// The declaration is DESCRIPTIVE ONLY. It does not authorize a request; a
/// permission-gated handler enforces it against the established caller before
/// touching protected data.
/// </summary>
public interface IQueryAuthorizationDeclaration
{
    static abstract QueryAuthorization Authorization { get; }
}
