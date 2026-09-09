using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ligature.Platform.Persistence.Database;

/// <summary>
/// The design-time factory <c>dotnet ef</c> uses, so <c>--project</c> stands
/// alone and there is no <c>--startup-project</c> (AGENTS.md section 3).
///
/// It reads LIGATURE_CONNECTION and has NO fallback. The previous version
/// hard-coded a localhost/postgres connection string, which meant
/// <c>dotnet ef database update</c> silently applied migrations to the
/// developer's own database no matter what the environment said — the
/// variable was documented as being read and was not. A migration applied to
/// the wrong database is not an error anyone sees until later, which is the
/// worst shape a defect can have in deployment tooling.
///
/// Failing is therefore the whole point: an unset variable stops the command
/// and names the fix, rather than choosing a target on the operator's behalf.
/// This mirrors the host, which refuses to start without a connection string
/// for the same reason (docs/architecture.md section 17).
///
/// The value is whatever credential the caller intends: the migrator runs as
/// migration_role, the test fixtures supply their own. This class does not
/// decide that.
/// </summary>
public sealed class LigatureDbContextFactory
    : IDesignTimeDbContextFactory<LigatureDbContext>
{
    internal const string ConnectionVariable = "LIGATURE_CONNECTION";

    public LigatureDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionVariable} is not set, so there is no database to "
                + "target. Set it to the connection the migration should be "
                + "applied with, for example:"
                + Environment.NewLine
                + Environment.NewLine
                + $"  export {ConnectionVariable}=\"Host=localhost;Port=5432;"
                + "Database=ligature;Username=migration_role;Password=...\""
                + Environment.NewLine
                + Environment.NewLine
                + "See AGENTS.md section 3. There is deliberately no default: "
                + "a fallback would silently migrate whichever database the "
                + "default happened to name.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<LigatureDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new LigatureDbContext(optionsBuilder.Options);
    }
}
