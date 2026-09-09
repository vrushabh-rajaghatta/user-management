using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Who the caller is, as established at authentication time.
///
/// One type rather than five loose members, because the frozen Audit model
/// treats these as a group: AR24 requires the actor snapshot columns to be
/// all-present or all-absent, and a partial snapshot is rejected by the
/// database. Modelling them individually would let the application assemble
/// exactly the half-built state the constraint exists to refuse.
///
/// These values are a SNAPSHOT, not a live view. They are captured once per
/// scope and never refreshed: an audit record must state who the actor was
/// when they acted, including a display name that has since changed. Audit's
/// AUD-1 puts it as "persisted exactly as captured"; the corollary here is
/// that nothing may re-read them from app_user later and call it the same
/// fact.
/// </summary>
/// <param name="DisplayName">
/// AR11 requires this whenever an actor is present.
/// </param>
/// <param name="Username">
/// Null for actor types that have none. A label, not an identifier — it is
/// transformed by anonymisation, while SubjectId is not (AUD-D29).
/// </param>
/// <param name="Email">
/// Human actors only. AR11 makes that a database CHECK, so supplying one for
/// any other actor type is rejected rather than ignored.
/// </param>
/// <param name="IdentityProvider">
/// Which system vouched for the actor: 'Application' for local credentials,
/// the provider name for external ones.
/// </param>
/// <param name="SubjectId">
/// The provider's permanent identifier for this subject. An identifier, so
/// anonymisation preserves it (invariant 12).
/// </param>
/// <param name="CapturedAt">
/// When this snapshot was built — once, at authentication. Distinct from the
/// audit record's own CapturedAt: the snapshot is built once per scope, the
/// record once per event. AR24 puts it in the all-or-nothing group.
/// </param>
public sealed record ActorIdentity(
    string DisplayName,
    string? Username,
    EmailAddress? Email,
    IdentityProvider IdentityProvider,
    string SubjectId,
    DateTimeOffset CapturedAt);
