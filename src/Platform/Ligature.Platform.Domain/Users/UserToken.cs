using Ligature.Platform.Domain.Provenance;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class UserToken : Entity<UserTokenId>
{
    // EF Core materialization only.
    private UserToken()
    {
    }
    private UserToken(
        UserTokenId id,
        UserIdentityId userIdentityId,
        TokenType tokenType,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset? usedAt,
        DateTimeOffset? invalidatedAt,
        DateTimeOffset createdAt,
        UserId createdBy)
        : base(id)
    {
        UserIdentityId = userIdentityId;
        TokenType = tokenType;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UsedAt = usedAt;
        InvalidatedAt = invalidatedAt;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public UserIdentityId UserIdentityId { get; }

    public TokenType TokenType { get; }

    public string TokenHash { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public UserId CreatedBy { get; }

    public static UserToken Create(
        UserTokenId id,
        UserIdentityId userIdentityId,
        TokenType tokenType,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        UserId createdBy)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException(
                "Token hash cannot be empty.");

        if (expiresAt <= createdAt)
            throw new DomainException(
                "Token expiry must be after creation.");

        return new UserToken(
            id,
            userIdentityId,
            tokenType,
            tokenHash,
            expiresAt,
            null,
            null,
            createdAt,
            createdBy);
    }

    public bool IsExpired(DateTimeOffset now)
        => now >= ExpiresAt;

    public bool IsUsable(DateTimeOffset now)
        => UsedAt is null
           && InvalidatedAt is null
           && now < ExpiresAt;

    public bool MarkUsed(DateTimeOffset usedAt)
    {
        if (UsedAt is not null || InvalidatedAt is not null)
            return false;

        if (usedAt >= ExpiresAt)
            return false;

        UsedAt = usedAt;

        return true;
    }

    public bool Invalidate(DateTimeOffset invalidatedAt)
    {
        if (UsedAt is not null || InvalidatedAt is not null)
            return false;

        InvalidatedAt = invalidatedAt;

        return true;
    }
}