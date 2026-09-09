using Ligature.Platform.Application.Users.Commands.CreateUser;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// Which commands emit which events — the static half of IMPL-08.
///
/// The catalogue is data and a handler's declarations are runtime calls, so
/// "every code a compiled handler declares exists and is active" cannot be
/// checked at start unless the declarations are also written down
/// statically. They are written down here, beside the handlers' own explicit
/// registration and in the same spirit: "which commands emit what" is a
/// question a reviewer answers by reading this class, not by reasoning about
/// what a handler might call.
///
/// Two checks depend on it. The host verifies at start that every code
/// listed here exists in the catalogue and is active, and refuses to run
/// otherwise — a deployment failure before any command runs, rather than a
/// runtime failure on the first affected command. And behaviour 7 refuses,
/// as a defect, an emission of a code the command did not list, so the list
/// cannot silently fall out of date.
///
/// A context may emit only codes it owns (emission contract). Every V1
/// command is User Management's; the context is recorded per entry so that
/// stops being an assumption the day a second context emits.
/// </summary>
public static class AuditDeclarations
{
    private static readonly IReadOnlyDictionary<Type, AuditDeclaration> ByCommand =
        new Dictionary<Type, AuditDeclaration>
        {
            // USR-C1 — one record per created row; TokenInvalidated (n) joins
            // when UT5 lands with CRD-C2.
            [typeof(CreateUserCommand)] = new(
                "UserManagement",
                ["UserCreated", "IdentityCreated", "TokenIssued"]),

            // PRV-C1 — the tenant's first record, emitted by provisioning
            // rather than by a command. Listed so the start-time check covers
            // it; provisioning validates against the release seed directly,
            // since it runs before any process could have loaded the
            // catalogue it is seeding.
            [typeof(PlatformProvisioning)] = new(
                "UserManagement",
                ["TenantProvisioned"]),
        };

    public static AuditDeclaration? For(Type commandType)
        => ByCommand.GetValueOrDefault(commandType);

    /// <summary>
    /// The start-time check. Throws with every mismatch named, and the host
    /// lets that stop the process (Program.cs).
    /// </summary>
    public static void VerifyAgainst(IAuditEventCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var failures = new List<string>();

        foreach (var (commandType, declaration) in ByCommand)
        {
            foreach (var code in declaration.Codes)
            {
                var definition = catalogue.Find(code, version: 1);

                if (definition is null)
                    failures.Add($"{commandType.Name} declares '{code}' v1, which the catalogue does not contain");
                else if (!definition.IsActive)
                    failures.Add($"{commandType.Name} declares '{code}' v1, which the catalogue has retired");
                else if (definition.OwningContext != declaration.OwningContext)
                    failures.Add($"{commandType.Name} ({declaration.OwningContext}) declares '{code}', owned by {definition.OwningContext}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The compiled handlers and the deployed audit catalogue disagree, "
                + "so this release cannot run against this database (IMPL-08). "
                + "Either the release's AUD-C3 migration has not been applied, or "
                + "a handler declares an event the release did not seed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures.Select(x => "  - " + x)));
        }
    }
}

/// <summary>A command's owning context and the catalogue codes it may emit.</summary>
public sealed record AuditDeclaration(string OwningContext, IReadOnlyList<string> Codes);

/// <summary>
/// A marker for the one emitter that is not a command: PRV-C1, which writes
/// TenantProvisioned from the provisioning tool on its own transaction.
/// </summary>
public static class PlatformProvisioning
{
}
