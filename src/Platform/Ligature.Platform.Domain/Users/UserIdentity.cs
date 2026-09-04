using Ligature.Platform.Domain.Provenance;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

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
        CreationStamp created,
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
        Created = created;
        Deactivation = deactivation;
    }

    public UserId UserId { get; }

    public ActorType ActorType { get; }

    public IdentityType IdentityType { get; }

    public IdentityProvider IdentityProvider { get; }

    public string SubjectId { get; }

    public string? Username { get; private set; }

    public UserStatus Status { get; private set; }

    public CreationStamp Created { get; }

    public DeactivationStamp? Deactivation { get; private set; }

    public static UserIdentity CreateLocal(
        UserIdentityId id,
        UserId userId,
        ActorType actorType,
        string username,
        CreationStamp created)
    {
        if (actorType == ActorType.System)
            throw new DomainException(
                "The system user cannot have an identity.");

        if (string.IsNullOrWhiteSpace(username))
            throw new DomainException(
                "Username cannot be empty.");

        ArgumentNullException.ThrowIfNull(created);

        return new UserIdentity(
            id,
            userId,
            actorType,
            IdentityType.Local,
            IdentityProvider.Application,
            id.Value.ToString(),
            username,
            UserStatus.Active,
            created,
            null);
    }

    public static UserIdentity CreateExternal(
     UserIdentityId id,
     UserId userId,
     ActorType actorType,
     IdentityProvider identityProvider,
     string subjectId,
     string? username,
     CreationStamp created)
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

        ArgumentNullException.ThrowIfNull(created);

        return new UserIdentity(
            id,
            userId,
            actorType,
            IdentityType.External,
            identityProvider,
            subjectId,
            username,
            UserStatus.Active,
            created,
            null);
    }

    public bool ChangeUsername(string newUsername)
    {
        if (IdentityType != IdentityType.Local)
            throw new DomainException(
                "Only local identities can change username.");

        if (string.IsNullOrWhiteSpace(newUsername))
            throw new DomainException("Username cannot be empty.");

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
    public void Reactivate()
    {
        if (Status == UserStatus.Active)
            throw new DomainException(
                "Identity is already active.");

        Status = UserStatus.Active;
        Deactivation = null;
    }
}