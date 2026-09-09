namespace Ligature.Platform.Domain.Audit;

/// <summary>
/// The Audit release constants (AUD-S09): values owned by the release, not by
/// configuration and not by the tenant database, for the reason
/// <see cref="Users.SecurityBaseline"/> gives — a value a tenant administrator
/// could reach is not a control, and a compiled constant is part of the
/// validated release artifact.
///
/// The single source of these values. AuditCatalogueSeeder seeds a new
/// tenant's retention policy v1 from here (AUD-C4 step 4), and the resolver
/// AUD-C2 and AUD-Q12 will share evaluates every tenant value against the same
/// floor (RT8), so a tenant is seeded at exactly the standard it is later
/// judged by. Nothing here knows about provisioning.
/// </summary>
public static class AuditReleaseBaseline
{
    /// <summary>
    /// The retention floor every tenant inherits: effective retention is
    /// max(this, the tenant's own value), never less (RT3, AUD-12).
    ///
    /// PLACEHOLDER — AUD-O11 is parked with Regulatory and has not been
    /// decided. 120 months is the figure the open-decisions register itself
    /// cites as common practice ("often ten years or more"), chosen so a
    /// development tenant reports a plausible policy rather than an absurd
    /// one. It is NOT a decided value. Code, tests and the validation protocol
    /// reference this constant by name, never the number, so closing AUD-O11
    /// changes one line here and nothing else (Design Specification section
    /// 10).
    /// </summary>
    public const int MinimumRetentionMonths = 120;
}
