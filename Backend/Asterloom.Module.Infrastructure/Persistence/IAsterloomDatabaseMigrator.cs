namespace Asterloom.Modules.Infrastructure.Persistence;

public interface IAsterloomDatabaseMigrator
{
    Task<DatabaseMigrationResult> MigrateAsync(CancellationToken cancellationToken);
}

public sealed record DatabaseMigrationResult(
    int AppliedCount,
    int PreviouslyAppliedCount,
    bool IsPersistent);
