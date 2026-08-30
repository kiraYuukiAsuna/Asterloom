using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Asterloom.Modules.Infrastructure.Persistence;

internal sealed class PostgreSqlDatabaseMigrator : IAsterloomDatabaseMigrator
{
    private static readonly Action<ILogger, string, int, string, Exception?> LogApplyingMigration =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Information,
            new EventId(2001, nameof(LogApplyingMigration)),
            "Applying database migration {Module}/{Version} ({MigrationName}).");

    private const string BootstrapSql =
        """
        CREATE SCHEMA IF NOT EXISTS infrastructure;

        CREATE TABLE IF NOT EXISTS infrastructure.schema_migrations (
            module_name text NOT NULL,
            version integer NOT NULL,
            migration_name text NOT NULL,
            checksum text NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now(),
            PRIMARY KEY (module_name, version)
        );
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgreSqlDatabaseMigrator> _logger;
    private readonly IAsterloomModuleMigration[] _migrations;

    public PostgreSqlDatabaseMigrator(
        NpgsqlDataSource dataSource,
        IEnumerable<IAsterloomModuleMigration> migrations,
        ILogger<PostgreSqlDatabaseMigrator> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
        _migrations = ValidateAndOrder(migrations);
    }

    public async Task<DatabaseMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText =
                "SELECT pg_advisory_xact_lock(hashtext('asterloom:schema-migrations'));";
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var bootstrapCommand = connection.CreateCommand())
        {
            bootstrapCommand.Transaction = transaction;
            bootstrapCommand.CommandText = BootstrapSql;
            await bootstrapCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var appliedMigrations = await ReadAppliedMigrationsAsync(
            connection,
            transaction,
            cancellationToken);
        var appliedCount = 0;

        foreach (var migration in _migrations)
        {
            var key = (migration.ModuleName, migration.Version);
            var checksum = ComputeChecksum(migration.Sql);
            if (appliedMigrations.TryGetValue(key, out var appliedChecksum))
            {
                if (!string.Equals(checksum, appliedChecksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Applied migration {migration.ModuleName}/{migration.Version} " +
                        "has changed. Add a new migration instead of editing migration history.");
                }

                continue;
            }

            LogApplyingMigration(
                _logger,
                migration.ModuleName,
                migration.Version,
                migration.Name,
                null);

            await using (var migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = migration.Sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recordCommand = connection.CreateCommand())
            {
                recordCommand.Transaction = transaction;
                recordCommand.CommandText =
                    """
                    INSERT INTO infrastructure.schema_migrations (
                        module_name,
                        version,
                        migration_name,
                        checksum)
                    VALUES (@module_name, @version, @migration_name, @checksum);
                    """;
                recordCommand.Parameters.AddWithValue("module_name", migration.ModuleName);
                recordCommand.Parameters.AddWithValue("version", migration.Version);
                recordCommand.Parameters.AddWithValue("migration_name", migration.Name);
                recordCommand.Parameters.AddWithValue("checksum", checksum);
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            appliedCount++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new DatabaseMigrationResult(
            appliedCount,
            _migrations.Length - appliedCount,
            IsPersistent: true);
    }

    private static IAsterloomModuleMigration[] ValidateAndOrder(
        IEnumerable<IAsterloomModuleMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        var ordered = migrations
            .OrderBy(static migration => migration.ModuleName, StringComparer.Ordinal)
            .ThenBy(static migration => migration.Version)
            .ToArray();

        foreach (var migration in ordered)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(migration.ModuleName);
            ArgumentException.ThrowIfNullOrWhiteSpace(migration.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(migration.Sql);
            if (migration.Version <= 0)
            {
                throw new InvalidOperationException(
                    $"Migration {migration.ModuleName}/{migration.Name} must have a positive version.");
            }
        }

        var duplicate = ordered
            .GroupBy(static migration => (migration.ModuleName, migration.Version))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Migration {duplicate.Key.ModuleName}/{duplicate.Key.Version} is registered more than once.");
        }

        return ordered;
    }

    private static async Task<Dictionary<(string ModuleName, int Version), string>>
        ReadAppliedMigrationsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT module_name, version, checksum FROM infrastructure.schema_migrations;";

        var result = new Dictionary<(string ModuleName, int Version), string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                (reader.GetString(0), reader.GetInt32(1)),
                reader.GetString(2));
        }

        return result;
    }

    private static string ComputeChecksum(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
