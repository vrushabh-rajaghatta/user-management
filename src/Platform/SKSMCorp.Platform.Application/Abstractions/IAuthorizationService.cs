namespace SKSMCorp.Platform.Application.Abstractions;

public interface IAuthorizationService
{
    Task<AuthorizationResult> IsAllowedAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Everything this actor effectively holds, at an instant.
    ///
    /// TWO VIEWS OVER ONE EVALUATION, not two authorisation rules. This and
    /// IsAllowedAsync share the actor gates and the candidate predicate; the
    /// single check narrows that set by scope and code, and this one does not
    /// narrow it at all. They must never disagree — a drift between them is
    /// silent in both directions, offering capabilities the server refuses or
    /// hiding ones it allows.
    ///
    /// In particular it carries AUT-C5's deliberate omission of role.IsActive
    /// from the shared part, so a retired role keeps authorising its existing
    /// holders here exactly as it does there.
    /// </summary>
    Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
        EffectivePermissionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// AUT-Q7 — everyone who could exercise one permission, at an instant.
    ///
    /// THE THIRD VIEW OVER THE SAME EVALUATION (RW2), and the reason it lives
    /// here rather than in a reader of its own:
    ///
    ///     IsAllowedAsync   one user,  one permission
    ///     EnumerateAsync   one user,  all permissions
    ///     WhoCanDoAsync    all users, one permission
    ///
    /// A separate reader would be a second authorisation rule, and the drift
    /// would be silent in both directions — naming people who cannot act, or
    /// omitting people who can. The actor gates are part of the shared
    /// evaluation for the same reason.
    ///
    /// Null when the permission code does not exist, which the route answers as
    /// 404 (RW8); empty when it exists and nobody holds it. A RETIRED
    /// permission exists and answers empty (RW9) — the shared predicate
    /// requires an active permission, and this query does not resurrect one.
    /// </summary>
    Task<IReadOnlyList<PermissionHolder>?> WhoCanDoAsync(
        WhoCanDoRequest request,
        CancellationToken cancellationToken);
}
