using Ligature.Platform.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Database;

/// <summary>
/// This class alone is NOT a complete runtime composition. It maps the schema;
/// it does not satisfy it. UpdatedAt/UpdatedBy are NOT NULL shadow properties
/// that nothing on this type populates, so a context constructed directly will
/// fail on the first save of an entity that declares them.
///
/// Runtime construction goes through AddPlatformPersistence, which supplies the
/// ProvenanceStampingInterceptor and the IClock it needs. That dependency is
/// deliberately not defaulted inside this class: constructing the interceptor
/// here would bypass DI and make the runtime composition harder to reason about
/// than a missing-registration failure at startup.
///
/// LigatureDbContextFactory is the sanctioned exception — it exists for
/// design-time migrations, which never call SaveChanges.
/// </summary>
public sealed class LigatureDbContext : DbContext
{
    public LigatureDbContext(DbContextOptions<LigatureDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
    }
}