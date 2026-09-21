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
}
