using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class Permission : Entity<PermissionId>
{
    private Permission(
        PermissionId id,
        string code,
        string name,
        string? description,
        string resource,
        string action,
        bool requiresHumanActor,
        bool isActive,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        Resource = resource;
        Action = action;
        RequiresHumanActor = requiresHumanActor;
        IsActive = isActive;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public string Code { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string Resource { get; }

    public string Action { get; }

    public bool RequiresHumanActor { get; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public static Permission Create(
        PermissionId id,
        string code,
        string name,
        string? description,
        string resource,
        string action,
        bool requiresHumanActor,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException(
                "Permission code cannot be empty.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(
                "Permission name cannot be empty.");

        if (string.IsNullOrWhiteSpace(resource))
            throw new DomainException(
                "Permission resource cannot be empty.");

        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException(
                "Permission action cannot be empty.");

        ArgumentNullException.ThrowIfNull(createdBy);

        return new Permission(
            id,
            code,
            name,
            description,
            resource,
            action,
            requiresHumanActor,
            isActive: true,
            createdAt,
            createdBy);
    }

    public void UpdateMetadata(
        string name,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(
                "Permission name cannot be empty.");

        Name = name;
        Description = description;
    }

    public void Deactivate()
    {
        if (!IsActive)
            throw new DomainException(
                "Permission is already inactive.");

        IsActive = false;
    }

    public void Reactivate()
    {
        if (IsActive)
            throw new DomainException(
                "Permission is already active.");

        IsActive = true;
    }
}