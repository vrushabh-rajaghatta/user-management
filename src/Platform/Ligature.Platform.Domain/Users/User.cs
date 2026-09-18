using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class User : AggregateRoot<UserId>
{
    // EF Core materialization only.
    private User() { }
    private User(
        UserId id,
        ActorType actorType,
        string? firstName,
        string? lastName,
        string displayName,
        EmailAddress? email,
        UserStatus status,
        DateTimeOffset createdAt,
        UserId createdBy,
        DeactivationStamp? deactivation)
        : base(id)
    {
        ActorType = actorType;
        FirstName = firstName;
        LastName = lastName;
        DisplayName = displayName;
        Email = email;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        Deactivation = deactivation;
    }

    public ActorType ActorType { get; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string DisplayName { get; private set; }

    public EmailAddress? Email { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public UserId CreatedBy { get; }

    public DeactivationStamp? Deactivation { get; private set; }

    public static User CreateHuman(
        UserId id,
        string firstName,
        string lastName,
        string displayName,
        string email,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("First name cannot be empty.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("Last name cannot be empty.");

        // ArgumentNullException.ThrowIfNull(created);

        return new User(
            id,
            ActorType.Human,
            firstName,
            lastName,
            displayName,
            EmailAddress.Create(email),
            UserStatus.Active,
            createdAt,
            createdBy,
            null);
    }

    public const string SystemDisplayName = "System";

    public static UserId SystemUserId { get; } =
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    public static User CreateSystem(
        DateTimeOffset createdAt)
    {
        return new User(
            SystemUserId,
            ActorType.System,
            null,
            null,
            SystemDisplayName,
            null,
            UserStatus.Active,
            createdAt,
            SystemUserId,
            null);
    }

    public bool ChangeEmail(EmailAddress newEmail)
    {
        ArgumentNullException.ThrowIfNull(newEmail);

        if (Email is not null && Email.Equals(newEmail))
            return false;

        Email = newEmail;

        return true;
    }

    /// <summary>The most a name may hold, in Unicode code points (USR-C2 G3).</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// USR-C2 (docs/requirements.md, "USR-C2 — Update User Profile", G3 as
    /// confirmed). Each name is TRIMMED, then VALIDATED, then STORED trimmed:
    /// required and non-blank, no control characters (char.IsControl, the
    /// definition EmailAddress uses), at most 100 Unicode CODE POINTS — what
    /// PostgreSQL's char_length counts, not string.Length's UTF-16 units.
    /// Interior whitespace is kept.
    ///
    /// Compared AFTER normalisation, so " Alice " against a stored "Alice" is
    /// no change: nothing is mutated and false is returned.
    ///
    /// USR-C1's CreateHuman does not apply these rules; that difference is
    /// known and deliberate until decided (Known Gaps).
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool UpdateProfile(
        string firstName,
        string lastName,
        string displayName)
    {
        firstName = NormalisedName("First name", firstName);
        lastName = NormalisedName("Last name", lastName);
        displayName = NormalisedName("Display name", displayName);

        var changed =
            FirstName != firstName ||
            LastName != lastName ||
            DisplayName != displayName;

        if (!changed)
            return false;

        FirstName = firstName;
        LastName = lastName;
        DisplayName = displayName;

        return true;
    }

    private static string NormalisedName(string field, string? value)
    {
        var normalised = (value ?? string.Empty).Trim();

        if (normalised.Length == 0)
            throw new DomainException($"{field} is required.");

        if (normalised.Any(char.IsControl))
            throw new DomainException($"{field} must not contain control characters.");

        if (normalised.EnumerateRunes().Count() > MaxNameLength)
            throw new DomainException($"{field} must be at most {MaxNameLength} characters.");

        return normalised;
    }

    public void Deactivate(DeactivationStamp deactivation)
    {
        ArgumentNullException.ThrowIfNull(deactivation);

        if (ActorType == ActorType.System)
            throw new DomainException("The system user cannot be deactivated.");

        if (Status == UserStatus.Inactive)
            throw new DomainException("User is already inactive.");

        // USR-C4 D9c. Exactly this rule and nothing broader: it does not
        // protect "the last administrator", which is a separate, undecided
        // policy (docs/requirements.md, "USR-C4 / USR-C5").
        if (deactivation.By == Id)
            throw new DomainException("A user cannot deactivate themselves.");

        Status = UserStatus.Inactive;
        Deactivation = deactivation;
    }

    public void Reactivate()
    {
        if (ActorType == ActorType.System)
            throw new DomainException("The system user cannot be reactivated.");

        if (Status == UserStatus.Active)
            throw new DomainException("User is already active.");

        Status = UserStatus.Active;
        Deactivation = null;
    }
}