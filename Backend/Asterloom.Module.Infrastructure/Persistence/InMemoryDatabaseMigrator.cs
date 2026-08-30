namespace Asterloom.Modules.Infrastructure.Persistence;

internal sealed class InMemoryDatabaseMigrator : IAsterloomDatabaseMigrator
{
    public Task<DatabaseMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DatabaseMigrationResult(0, 0, IsPersistent: false));
    }
}
