using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Domain.Users;

public sealed class UserIdentity : AggregateRoot<UserIdentityId>
{
    // EF Core materialization only.
    private UserIdentity()
    {
    }
    private UserIdentity(
        UserIdentityId id,
        UserId userId,
        ActorType actorType,
        IdentityType identityType,
        IdentityProvider identityProvider,
        string subjectId,
        string? username,
        UserStatus status,
        DateTimeOffset createdAt,
UserId createdBy,
        DeactivationStamp? deactivation)
        : base(id)
    {
        UserId = userId;
        ActorType = actorType;
        IdentityType = identityType;
        IdentityProvider = identityProvider;
        SubjectId = subjectId;
        Username = username;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        Deactivation = deactivation;
    }

    public UserId UserId { get; }

    public ActorType ActorType { get; }

    public IdentityType IdentityType { get; }

    public IdentityProvider IdentityProvider { get; }

    public string SubjectId { get; }

    public string? Username { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public UserId CreatedBy { get; }

    public DeactivationStamp? Deactivation { get; private set; }

    public static UserIdentity CreateLocal(
        UserIdentityId id,
        UserId userId,
        ActorType actorType,
        string username,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (actorType == ActorType.System)
            throw new DomainException(
                "The system user cannot have an identity.");

        ValidateUsernameBoundary(username);

        // ArgumentNullException.ThrowIfNull(created);

        return new UserIdentity(
            id,
            userId,
            actorType,
            IdentityType.Local,
            IdentityProvider.Application,
            id.Value.ToString(),
            username,
            UserStatus.Active,
            createdAt,
            createdBy,
            null);
    }

    public static UserIdentity CreateExternal(
     UserIdentityId id,
     UserId userId,
     ActorType actorType,
     IdentityProvider identityProvider,
     string subjectId,
     string? username,
     DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (actorType == ActorType.System)
            throw new DomainException(
                "The system user cannot have an identity.");

        ArgumentNullException.ThrowIfNull(identityProvider);

        if (identityProvider.Equals(IdentityProvider.Application))
            throw new DomainException(
                "External identity cannot use the Application provider.");

        if (string.IsNullOrWhiteSpace(subjectId))
            throw new DomainException(
                "Subject ID cannot be empty.");

        // ArgumentNullException.ThrowIfNull(created);

        return new UserIdentity(
            id,
            userId,
            actorType,
            IdentityType.External,
            identityProvider,
            subjectId,
            username,
            UserStatus.Active,
            createdAt,
            createdBy,
            null);
    }

    /// <summary>
    /// THE local username rule (docs/requirements.md, "Local usernames refuse
    /// surrounding whitespace", WS1 and WS2), and its single source of truth:
    /// CreateLocal and ChangeUsername apply it, and USR-C1, IDN-Q3 and
    /// provisioning call it rather than reproducing it.
    ///
    /// A username is an identifier, so a value Trim() would change is REFUSED,
    /// never trimmed. Whitespace is exactly char.IsWhiteSpace, the set Trim()
    /// removes; invisible format characters such as U+200B are not in it and
    /// are deliberately not this rule's business (WS9). Whitespace inside a
    /// username is untouched, and case is the UI7 index's concern, not this.
    ///
    /// ck_user_identity_local_username_no_surrounding_whitespace names the
    /// same 25 characters as the database's backstop; a test holds the two to
    /// the same answer for every UTF-16 code unit.
    /// </summary>
    public static void ValidateUsernameBoundary(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException("Username cannot be empty.");

        if (!string.Equals(username, username.Trim(), StringComparison.Ordinal))
            throw new DomainException("A username cannot begin or end with whitespace.");
    }

    public bool ChangeUsername(string newUsername)
    {
        if (IdentityType != IdentityType.Local)
            throw new DomainException(
                "Only local identities can change username.");

        ValidateUsernameBoundary(newUsername);

        if (string.Equals(
                Username,
                newUsername,
                StringComparison.OrdinalIgnoreCase))
            return false;

        Username = newUsername;

        return true;
    }

    public void Deactivate(DeactivationStamp deactivation)
    {
        ArgumentNullException.ThrowIfNull(deactivation);

        if (Status == UserStatus.Inactive)
            throw new DomainException(
                "Identity is already inactive.");

        Status = UserStatus.Inactive;
        Deactivation = deactivation;
    }
    /// <summary>
    /// USR-C5 (D5): reactivates this identity only if it was deactivated by the
    /// same operation as its user — the same instant and the same actor. An
    /// identity deactivated on its own (a future IDN-C3) carries a different
    /// stamp, and returning the user must not undo that decision.
    /// </summary>
    /// <returns>Whether the identity was reactivated.</returns>
    public bool ReactivateWith(DeactivationStamp userDeactivation)
    {
        ArgumentNullException.ThrowIfNull(userDeactivation);

        if (Status != UserStatus.Inactive || Deactivation != userDeactivation)
            return false;

        Reactivate();

        return true;
    }

    public void Reactivate()
    {
        if (Status == UserStatus.Active)
            throw new DomainException(
                "Identity is already active.");

        Status = UserStatus.Active;
        Deactivation = null;
    }
}