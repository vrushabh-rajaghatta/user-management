using Ligature.Platform.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Database;

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