using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// The assignment under which an authorised act was permitted — captured at
/// the authorisation boundary, where it is known, rather than reconstructed
/// afterwards.
///
/// Reconstruction is the thing this exists to avoid. Answering "what
/// authorised this?" later means re-running the authorisation predicate
/// against role and grant tables that have since changed, which produces
/// today's answer to a question about the past. Audit consumes this instead,
/// and must never query user_role to work it out for itself.
///
/// Both the id and the name are carried. The id is the stable reference; the
/// name is what a reader sees, and renaming a role (AUT-C4) must not silently
/// rewrite the authority recorded against acts already performed.
/// </summary>
/// <param name="RoleId">The role that carried the permission.</param>
/// <param name="RoleName">
/// Its name at the moment of the act, captured for the reason above.
/// </param>
/// <param name="ScopeType">Global in V1.</param>
/// <param name="ScopeId">Null when the scope is Global (AR12).</param>
/// <param name="AssignmentId">
/// The user_role row itself. Closes AUD-O1: with it, point-in-time
/// reconstruction (REV-Q6) is a join rather than a search.
/// </param>
public sealed record AuthorizingAssignment(
    RoleId RoleId,
    string RoleName,
    ScopeType ScopeType,
    Guid? ScopeId,
    UserRoleId AssignmentId);
