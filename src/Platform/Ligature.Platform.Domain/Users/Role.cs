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
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(
                "Role name cannot be empty.");

        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException(
                "Role code cannot be empty.");

        ArgumentNullException.ThrowIfNull(createdBy);

        return new Role(
            id,
            name,
            code,
            description,
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

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(
                "Role name cannot be empty.");

        Name = name;
        Description = description;
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