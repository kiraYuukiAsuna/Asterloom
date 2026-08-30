using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Asterloom.Modules.Identity.Persistence;

public sealed class AsterloomIdentityDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AsterloomIdentityDbContext>
{
    public AsterloomIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AsterloomIdentityDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=asterloom_design;Username=asterloom;Password=not-used",
                postgres => postgres.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    IdentityPersistence.Schema))
            .UseOpenIddict()
            .Options;

        return new AsterloomIdentityDbContext(options);
    }
}
