using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// The identity a session is bound to, and that session's timing, read live
/// (B6-B, D5).
///
/// A DEDICATED read rather than IExecutionContext.Identity. ActorIdentity is
/// request-scoped and therefore current, so this is not about staleness: it is
/// that ActorIdentity exists to serve Audit, carries no UserIdentityId, and an
/// audit-driven change to it must not silently change an API contract.
///
/// Identity and timing come from one read because they come from one join, and
/// a query that needs internal consistency obtains it through its own read
/// rather than by borrowing the command transaction boundary (D7).
/// </summary>
public interface IAuthenticatedCallerReader
{
    Task<AuthenticatedCaller?> ReadAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken);
}

/// <param name="UserIdentityId">
/// The identity THIS SESSION was established with. A user may hold several,
/// and reporting any other would answer a different question (D3).
/// </param>
public sealed record AuthenticatedCaller(
    UserIdentityId UserIdentityId,
    string Username,
    string DisplayName,
    DateTimeOffset SessionExpiresAt,
    DateTimeOffset SessionLastActivityAt);
