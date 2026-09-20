using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class Role : AggregateRoot<RoleId>
{
    private Role(
        RoleId id,
        string name,
        string code,
        string? description,
        bool isSystemRole,
        bool isActive,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        Name = name;
        Code = code;
        Description = description;
        IsSystemRole = isSystemRole;
        IsActive = isActive;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public string Name { get; private set; }

    public string Code { get; private set; }

    public string? Description { get; private set; }

    public bool IsSystemRole { get; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public static Role Create(
        RoleId id,
        string name,
        string code,
        string? description,
        bool isSystemRole,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        ValidateCode(code);

        ArgumentNullException.ThrowIfNull(createdBy);

        // RC2 governs what a TENANT may supply (AUT-C3). A release-owned seed
        // is the product's own text, governed by the release and by PRV-C2,
        // which REFUSES rather than reconciles a role's metadata — two seeded
        // descriptions are longer than a tenant may write, and editing them
        // would refuse deployment on every existing database.
        return new Role(
            id,
            isSystemRole ? Required(name, "A role name") : NormalisedName(name),
            code,
            isSystemRole ? description : NormalisedDescription(description),
            isSystemRole,
            isActive: true,
            createdAt,
            createdBy);
    }

    public void UpdateMetadata(
        string name,
        string? description)
    {
        if (IsSystemRole)
            throw new DomainException(
                "System roles cannot be modified.");

        Name = NormalisedName(name);
        Description = NormalisedDescription(description);
    }


    /// <summary>The most a role's name or description may hold, in Unicode code points (RC2).</summary>
    public const int MaxTextLength = 100;

    /// <summary>
    /// AUT-C3's code rule (docs/requirements.md, "AUT-C3 CreateRole", RC1).
    ///
    /// A code is an IDENTIFIER: refused, never normalised, exactly as a local
    /// username is. Surrounding whitespace is char.IsWhiteSpace — the set
    /// Trim() removes — and there is deliberately no grammar beyond that: no
    /// case folding, no allowed-character set. Uniqueness, including the
    /// case-only collision, belongs to the command and the index; a code on its
    /// own cannot know what else exists.
    /// </summary>
    public static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("A role code is required.");

        if (!string.Equals(code, code.Trim(), StringComparison.Ordinal))
            throw new DomainException("A role code cannot begin or end with whitespace.");
    }

    /// <summary>
    /// RC2: the USR-C2 family, so this is not a third validation dialect.
    /// Trimmed, then required, no control characters, at most 100 code points,
    /// and the TRIMMED value is what is stored.
    /// </summary>
    /// <summary>The one rule every role shares: it is called something.</summary>
    private static string Required(string? value, string subject)
        => string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"{subject} is required.")
            : value;

    private static string NormalisedName(string? value)
    {
        var normalised = (value ?? string.Empty).Trim();

        if (normalised.Length == 0)
            throw new DomainException("A role name is required.");

        Check(normalised, "A role name");

        return normalised;
    }

    /// <summary>As the name, except that nothing is a valid description (RC2).</summary>
    private static string? NormalisedDescription(string? value)
    {
        var normalised = (value ?? string.Empty).Trim();

        if (normalised.Length == 0)
            return null;

        Check(normalised, "A role description");

        return normalised;
    }

    private static void Check(string value, string subject)
    {
        if (value.Any(char.IsControl))
            throw new DomainException($"{subject} must not contain control characters.");

        if (value.EnumerateRunes().Count() > MaxTextLength)
            throw new DomainException($"{subject} must be at most {MaxTextLength} characters.");
    }

    public void Deactivate()
    {
        if (IsSystemRole)
            throw new DomainException(
                "System roles cannot be deactivated.");

        if (!IsActive)
            throw new DomainException(
                "Role is already inactive.");

        IsActive = false;
    }

    public void Reactivate()
    {
        if (IsSystemRole)
            throw new DomainException(
                "System roles cannot be reactivated.");

        if (IsActive)
            throw new DomainException(
                "Role is already active.");

        IsActive = true;
    }
}