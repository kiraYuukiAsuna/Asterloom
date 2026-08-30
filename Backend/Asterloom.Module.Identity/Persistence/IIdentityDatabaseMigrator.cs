using Microsoft.EntityFrameworkCore;

namespace Asterloom.Modules.Identity.Persistence;

public interface IIdentityDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}

internal sealed class IdentityDatabaseMigrator(
    AsterloomIdentityDbContext context,
    IdentityPersistenceOptions options) : IIdentityDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if (options.Provider == IdentityPersistenceProvider.Memory)
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        await context.Database.MigrateAsync(cancellationToken);
    }
}
