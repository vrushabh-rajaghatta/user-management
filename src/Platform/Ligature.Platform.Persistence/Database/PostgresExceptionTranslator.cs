using Ligature.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ligature.Platform.Persistence.Database;

/// <summary>
/// Turns the database's rejection of a known race into a domain error.
///
/// The catalogue's rule is that the database is the real enforcer for
/// uniqueness and overlap, and "handlers translate violations; they do not
/// pre-check and hope". A repository pre-check produces a friendly message for
/// the common case; this produces the same class of error for the case the
/// pre-check cannot close, so a caller cannot tell which one fired.
///
/// DELIBERATELY NARROW. Only constraints listed below are translated; anything
/// else is rethrown untouched, and that is the whole design. A generic
/// "business rule violated" fallback would flatten together things that are not
/// alike: an expected race, a programming error, a broken invariant, an
/// unexpected database failure, an infrastructure fault. A ck_ violation means
/// the code built a row the model forbids — a bug — and dressing it as user
/// error would send someone looking at their own input instead of at the stack
/// trace.
/// </summary>
internal static class PostgresExceptionTranslator
{
    /// <summary>
    /// Only constraints a command can actually reach today. The role-overlap
    /// exclusion constraints and RP2's live-grant index are deliberately
    /// absent: no command triggers them yet, so their wording would be written
    /// blind and untested. They belong to AUT-C1 and AUT-C7.
    /// </summary>
    private static readonly Dictionary<string, string> KnownViolations =
        new(StringComparer.Ordinal)
        {
            // AU3
            ["ux_app_user_active_human_email"] =
                "A user with this email address already exists.",

            // UI7
            ["ux_user_identity_local_username"] =
                "A user identity with this username already exists.",
        };

    /// <summary>
    /// Returns the domain error a known constraint violation maps to, or null
    /// when the exception should propagate exactly as it arrived.
    /// </summary>
    internal static BusinessRuleViolationException? Translate(
        DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgres)
            return null;

        // Dispatch on the constraint NAME alone, not on SqlState.
        //
        // An earlier version also required 23505. That was redundant — our
        // constraint names are globally distinctive, so the name already
        // identifies the rule — and worse, it was a trap for the next entries
        // this map is expected to gain. The role-overlap constraints AUT-C1
        // will add raise 23P01, not 23505, so a hard-coded unique-violation
        // check would have made those mappings silently never fire.
        //
        // Errors that carry no constraint name, such as a NOT NULL violation,
        // fall through here and propagate untouched.
        if (postgres.ConstraintName is null)
            return null;

        return KnownViolations.TryGetValue(postgres.ConstraintName, out var message)
            ? new BusinessRuleViolationException(message)
            : null;
    }
}
