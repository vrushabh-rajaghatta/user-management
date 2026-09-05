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

    public bool ChangeEmail(EmailAddress newEmail)
    {
        ArgumentNullException.ThrowIfNull(newEmail);

        if (Email is not null && Email.Equals(newEmail))
            return false;

        Email = newEmail;

        return true;
    }

    public bool UpdateProfile(
    string firstName,
    string lastName,
    string displayName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("First name cannot be empty.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("Last name cannot be empty.");

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

    public void Deactivate(DeactivationStamp deactivation)
    {
        ArgumentNullException.ThrowIfNull(deactivation);

        if (ActorType == ActorType.System)
            throw new DomainException("The system user cannot be deactivated.");

        if (Status == UserStatus.Inactive)
            throw new DomainException("User is already inactive.");

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