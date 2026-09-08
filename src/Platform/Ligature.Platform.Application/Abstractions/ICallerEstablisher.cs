using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Turns a SessionId into an established caller, or does nothing
/// (docs/architecture.md section 17).
///
/// This is the User Management side of the ownership boundary. The Host owns
/// bearer extraction, signature verification and carrier issuance; it must not
/// decide independently whether a session is live or whether a user has been
/// deactivated. It extracts a SessionId and asks this.
///
/// Deliberately NOT a command. It is authenticated-request infrastructure that
/// runs before the pipeline, not business work: there is no RecordActivity
/// command, and a command per API call would be absurd. It also cannot be a
/// command without circularity — the pipeline's AuthenticationBehavior is the
/// thing this exists to satisfy.
/// </summary>
public interface ICallerEstablisher
{
    /// <summary>
    /// Returns whether a caller was established, and NEVER throws for an
    /// invalid session.
    ///
    /// The bool is the entire contract. Malformed carrier, unknown session,
    /// revoked, expired, idle, inactive user and inactive identity all return
    /// false, and the reason does not cross this boundary. Session identifiers
    /// are supplied by callers, so any observable difference between those
    /// states would let one be probed for the others — a richer result type
    /// would be exactly the oracle section 17 forbids, and would eventually be
    /// logged, returned or branched on.
    ///
    /// Returning rather than throwing is also deliberate: this runs as
    /// middleware, and an exception escaping to a client is how internal detail
    /// leaks. A false result establishes no caller and lets the command
    /// pipeline decide — anonymous commands still run, authenticated ones are
    /// refused by AuthenticationBehavior.
    /// </summary>
    Task<bool> EstablishAsync(
        UserSessionId sessionId,
        CancellationToken cancellationToken);
}
