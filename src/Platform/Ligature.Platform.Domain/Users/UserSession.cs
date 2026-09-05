using System.Net;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class UserSession : Entity<UserSessionId>
{
    private UserSession(
        UserSessionId id,
        UserIdentityId userIdentityId,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt,
        UserId? revokedBy,
        string? revocationReason,
        IPAddress? ipAddress,
        string? userAgent)
        : base(id)
    {
        UserIdentityId = userIdentityId;
        CreatedAt = createdAt;
        LastActivityAt = lastActivityAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevocationReason = revocationReason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
    }

    public UserIdentityId UserIdentityId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public UserId? RevokedBy { get; private set; }

    public string? RevocationReason { get; private set; }

    public IPAddress? IpAddress { get; }

    public string? UserAgent { get; }

    public static UserSession Create(
        UserSessionId id,
        UserIdentityId userIdentityId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        IPAddress? ipAddress,
        string? userAgent)
    {
        if (expiresAt <= createdAt)
            throw new DomainException(
                "Session expiry must be after session creation.");

        return new UserSession(
            id,
            userIdentityId,
            createdAt,
            createdAt,
            expiresAt,
            null,
            null,
            null,
            ipAddress,
            userAgent);
    }

    public bool IsExpired(DateTimeOffset now)
        => now >= ExpiresAt;

    public bool IsRevoked()
        => RevokedAt is not null;

    public bool IsActive(
        DateTimeOffset now,
        TimeSpan idleTimeout)
        => RevokedAt is null
           && now < ExpiresAt
           && now < LastActivityAt + idleTimeout;

    public void RecordActivity(DateTimeOffset activityAt)
    {
        if (activityAt < LastActivityAt)
            throw new DomainException(
                "Session activity cannot move backwards.");

        if (activityAt >= ExpiresAt)
            throw new DomainException(
                "Activity cannot be recorded after session expiry.");

        LastActivityAt = activityAt;
    }

    public bool Revoke(
        DateTimeOffset revokedAt,
        UserId revokedBy,
        string revocationReason)
    {
        ArgumentNullException.ThrowIfNull(revokedBy);

        if (string.IsNullOrWhiteSpace(revocationReason))
            throw new DomainException(
                "Revocation reason cannot be empty.");

        if (RevokedAt is not null)
            return false;

        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevocationReason = revocationReason;

        return true;
    }
}