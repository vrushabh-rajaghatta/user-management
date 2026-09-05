using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ligature.Platform.Persistence.Database;

public sealed class LigatureDbContextFactory
    : IDesignTimeDbContextFactory<LigatureDbContext>
{
    public LigatureDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LigatureDbContext>();

        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=ligature;Username=postgres;Password=postgres");

        return new LigatureDbContext(optionsBuilder.Options);
    }
}