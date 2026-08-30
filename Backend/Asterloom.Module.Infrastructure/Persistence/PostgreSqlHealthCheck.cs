using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Asterloom.Modules.Infrastructure.Persistence;

internal sealed class PostgreSqlHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1;");
            command.CommandTimeout = 3;
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception) when (
            exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL is unavailable.",
                exception);
        }
    }
}
