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

    /// <summary>
    /// Replaces the stored representation of the SAME password, after a
    /// successful sign-in found the algorithm stale (SES-C1 step 5).
    ///
    /// Deliberately narrow, and deliberately not ChangePassword. The password
    /// did not change, so:
    ///
    /// PasswordChangedAt must not move — the frozen model says it "drives
    /// expiry policy if introduced", and a rehash silently resetting a
    /// password's age would hand every user an indefinite extension.
    ///
    /// The lockout counters must not be touched here. Clearing them is the
    /// successful-sign-in transition (step 6), which the caller performs
    /// explicitly; folding it into a rehash would make the reset depend on
    /// whether the algorithm happened to be stale.
    ///
    /// No password_history row belongs to a rehash either. CR5 requires history
    /// on every password SET or CHANGE; this is neither, and writing one would
    /// record the same password twice and shorten the reuse window.
    /// </summary>
    public void RehashPassword(
        string passwordHash,
        string passwordAlgorithm)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException(
                "Password hash cannot be empty.");

        if (string.IsNullOrWhiteSpace(passwordAlgorithm))
            throw new DomainException(
                "Password algorithm cannot be empty.");

        PasswordHash = passwordHash;
        PasswordAlgorithm = passwordAlgorithm;
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