using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class Credential : AggregateRoot<CredentialId>
{
    private Credential(
        CredentialId id,
        UserIdentityId userIdentityId,
        IdentityType identityType,
        string passwordHash,
        string passwordAlgorithm,
        DateTimeOffset passwordChangedAt,
        bool mustChangePassword,
        int failedAttemptCount,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        UserIdentityId = userIdentityId;
        IdentityType = identityType;
        PasswordHash = passwordHash;
        PasswordAlgorithm = passwordAlgorithm;
        PasswordChangedAt = passwordChangedAt;
        MustChangePassword = mustChangePassword;
        FailedAttemptCount = failedAttemptCount;
        LockedUntil = lockedUntil;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public UserIdentityId UserIdentityId { get; }

    public IdentityType IdentityType { get; }

    public string PasswordHash { get; private set; }

    public string PasswordAlgorithm { get; private set; }

    public DateTimeOffset PasswordChangedAt { get; private set; }

    public bool MustChangePassword { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public UserId CreatedBy { get; }

    public static Credential Create(
        CredentialId id,
        UserIdentityId userIdentityId,
        IdentityType identityType,
        string passwordHash,
        string passwordAlgorithm,
        DateTimeOffset passwordChangedAt,
        bool mustChangePassword,
        DateTimeOffset createdAt,
        UserId createdBy)
    {
        if (identityType != IdentityType.Local)
            throw new DomainException(
                "Credentials can only belong to local identities.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException(
                "Password hash cannot be empty.");

        if (string.IsNullOrWhiteSpace(passwordAlgorithm))
            throw new DomainException(
                "Password algorithm cannot be empty.");

        return new Credential(
            id,
            userIdentityId,
            identityType,
            passwordHash,
            passwordAlgorithm,
            passwordChangedAt,
            mustChangePassword,
            0,
            null,
            createdAt,
            createdBy);
    }

    public void ChangePassword(
        string passwordHash,
        string passwordAlgorithm,
        DateTimeOffset changedAt,
        bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException(
                "Password hash cannot be empty.");

        if (string.IsNullOrWhiteSpace(passwordAlgorithm))
            throw new DomainException(
                "Password algorithm cannot be empty.");

        PasswordHash = passwordHash;
        PasswordAlgorithm = passwordAlgorithm;
        PasswordChangedAt = changedAt;
        MustChangePassword = mustChangePassword;
        FailedAttemptCount = 0;
        LockedUntil = null;
    }

    public void ClearMustChangePassword()
    {
        MustChangePassword = false;
    }

    public void RecordFailedAttempt()
    {
        FailedAttemptCount++;
    }

    public void Lock(DateTimeOffset lockedUntil)
    {
        LockedUntil = lockedUntil;
    }

    public void Unlock()
    {
        LockedUntil = null;
        FailedAttemptCount = 0;
    }
}