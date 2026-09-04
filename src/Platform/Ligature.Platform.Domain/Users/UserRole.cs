using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class UserRole : Entity<UserRoleId>
{
    // EF Core materialization only.
    private UserRole()
    {
    }
    private UserRole(
        UserRoleId id,
        UserId userId,
        ActorType actorType,
        RoleId roleId,
        ScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset assignedAt,
        UserId assignedBy,
        string assignmentReason,
        DateTimeOffset? revokedAt,
        UserId? revokedBy,
        string? revocationReason,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        UserId = userId;
        ActorType = actorType;
        RoleId = roleId;
        ScopeType = scopeType;
        ScopeId = scopeId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        AssignedAt = assignedAt;
        AssignedBy = assignedBy;
        AssignmentReason = assignmentReason;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevocationReason = revocationReason;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public UserId UserId { get; }

    public ActorType ActorType { get; }

    public RoleId RoleId { get; }

    public ScopeType ScopeType { get; }

    public Guid? ScopeId { get; }

    public DateTimeOffset EffectiveFrom { get; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public DateTimeOffset AssignedAt { get; }

    public UserId AssignedBy { get; }

    public string AssignmentReason { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public string? RevocationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public static UserRole Create(
        UserRoleId id,
        UserId userId,
        ActorType actorType,
        RoleId roleId,
        ScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset assignedAt,
        UserId assignedBy,
        string assignmentReason,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(scopeType);
        ArgumentNullException.ThrowIfNull(assignedBy);
        ArgumentNullException.ThrowIfNull(createdBy);

        if (actorType == ActorType.System)
            throw new DomainException(
                "The system user cannot hold roles.");

        if (scopeType.Equals(ScopeType.Global) && scopeId is not null)
            throw new DomainException(
                "Global scope cannot have a scope ID.");

        if (!scopeType.Equals(ScopeType.Global) && scopeId is null)
            throw new DomainException(
                "Non-global scope requires a scope ID.");

        if (effectiveTo is not null &&
            effectiveTo < effectiveFrom)
        {
            throw new DomainException(
                "Effective end cannot be before effective start.");
        }

        if (actorType == ActorType.Agent &&
            effectiveTo is null)
        {
            throw new DomainException(
                "Agent role assignments must have a finite effective end.");
        }

        if (string.IsNullOrWhiteSpace(assignmentReason))
            throw new DomainException(
                "Assignment reason cannot be empty.");

        return new UserRole(
            id,
            userId,
            actorType,
            roleId,
            scopeType,
            scopeId,
            effectiveFrom,
            effectiveTo,
            assignedAt,
            assignedBy,
            assignmentReason,
            revokedAt: null,
            revokedBy: null,
            revocationReason: null,
            createdAt,
            createdBy);
    }

    public void Revoke(
        DateTimeOffset revokedAt,
        UserId revokedBy,
        string revocationReason)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);

        if (RevokedAt is not null)
            throw new DomainException(
                "Role assignment has already been revoked.");

        if (string.IsNullOrWhiteSpace(revocationReason))
            throw new DomainException(
                "Revocation reason cannot be empty.");

        if (revokedAt < AssignedAt)
            throw new DomainException(
                "Revocation cannot occur before assignment.");

        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevocationReason = revocationReason;
        EffectiveTo = revokedAt;
    }
}