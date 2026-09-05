using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class PasswordHistory : Entity<PasswordHistoryId>
{
    private PasswordHistory(
        PasswordHistoryId id,
        UserIdentityId userIdentityId,
        string passwordHash,
        string passwordAlgorithm,
        DateTimeOffset createdAt)
        : base(id)
    {
        UserIdentityId = userIdentityId;
        PasswordHash = passwordHash;
        PasswordAlgorithm = passwordAlgorithm;
        CreatedAt = createdAt;
    }

    public UserIdentityId UserIdentityId { get; }

    public string PasswordHash { get; }

    public string PasswordAlgorithm { get; }

    public DateTimeOffset CreatedAt { get; }

    public static PasswordHistory Create(
        PasswordHistoryId id,
        UserIdentityId userIdentityId,
        string passwordHash,
        string passwordAlgorithm,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException(
                "Password hash cannot be empty.");

        if (string.IsNullOrWhiteSpace(passwordAlgorithm))
            throw new DomainException(
                "Password algorithm cannot be empty.");

        return new PasswordHistory(
            id,
            userIdentityId,
            passwordHash,
            passwordAlgorithm,
            createdAt);
    }
}