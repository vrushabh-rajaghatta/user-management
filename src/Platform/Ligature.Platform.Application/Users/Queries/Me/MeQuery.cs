using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Users.Queries.Me;

/// <summary>
/// Who is the caller for this request? (B6-B.)
///
/// The SessionId arrives from the carrier the Host already verified, exactly as
/// SignOutCommand takes it — a client cannot name someone else's session. It is
/// the KEY FOR OBTAINING the session's timing, and not an invitation to decide
/// again whether that session is valid: caller establishment is the
/// authentication boundary and has already run (D4).
/// </summary>
public sealed record MeQuery(UserSessionId SessionId) : IQuery<MeResult>;
