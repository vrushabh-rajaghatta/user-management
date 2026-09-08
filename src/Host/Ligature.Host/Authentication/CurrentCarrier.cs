using Ligature.Platform.Domain.Users;

namespace Ligature.Host.Authentication;

/// <summary>
/// The SessionId recovered from this request's carrier, if there was one.
///
/// Scoped per request, and separate from IExecutionContext deliberately: the
/// execution context carries the authenticated ACTOR, which is application
/// state, while this carries authentication TRANSPORT state, which is the
/// Host's. SES-C2 takes a SessionId as an explicit input rather than reading
/// one from the execution context precisely so the application layer stays free
/// of the access-token architecture; putting the SessionId into the execution
/// context to save passing it would undo that.
///
/// Set only by CallerMiddleware, and only for a carrier whose signature
/// verified — but a set value still says nothing about whether the session is
/// live. That question belongs to ICallerEstablisher.
/// </summary>
public sealed class CurrentCarrier
{
    public UserSessionId? SessionId { get; private set; }

    public void Set(UserSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        SessionId = sessionId;
    }
}
