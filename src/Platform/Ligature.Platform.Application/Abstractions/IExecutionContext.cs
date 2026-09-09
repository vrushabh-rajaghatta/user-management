using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// The caller for the current scope: who they are, and what authorised the
/// act they are performing.
///
/// Assembled in two stages, deliberately. Identity is established at
/// authentication, before the command pipeline runs; authority is established
/// by the authorisation check, part-way through it. The Audit model requires
/// exactly this ordering (IMPL-05): a refused command must still carry the
/// caller's identity, with the authorising role left null, or a refusal is
/// evidence of nothing.
///
/// The two stages have different rules, and the difference matters:
///
///   Identity  — established once, atomically, and can never change.
///   Authority — established at most once, and is legitimately absent.
///
/// Absent authority is not a half-built context. Sign-in, self-service and
/// token-bearer commands are authenticated but authorised by no role
/// (AUD-D28), and a refused command has an identity and no authority by
/// definition.
/// </summary>
public interface IExecutionContext
{
    UserId UserId { get; }

    ActorType ActorType { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// The actor snapshot's identity half, captured at authentication and
    /// never refreshed. Throws when no caller is established, for the same
    /// reason <see cref="UserId"/> does: there is no honest placeholder.
    /// </summary>
    ActorIdentity Identity { get; }

    /// <summary>
    /// The assignment that authorised this act, or null when nothing did.
    /// Null is a real answer here and is NOT an error — see the type remarks.
    /// </summary>
    AuthorizingAssignment? Authority { get; }
}
