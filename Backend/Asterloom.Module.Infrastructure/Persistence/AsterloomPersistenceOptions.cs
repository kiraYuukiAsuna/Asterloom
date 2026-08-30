using Microsoft.Extensions.Configuration;

namespace Asterloom.Modules.Infrastructure.Persistence;

public enum AsterloomPersistenceProvider
{
    Memory = 0,
    PostgreSql = 1,
}

public sealed record AsterloomPersistenceOptions(
    AsterloomPersistenceProvider Provider,
    string? ConnectionString)
{
    public static AsterloomPersistenceOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var providerValue = configuration["Persistence:Provider"] ?? "PostgreSql";
        if (!Enum.TryParse<AsterloomPersistenceProvider>(
                providerValue,
                ignoreCase: true,
                out var provider))
        {
            throw new InvalidOperationException(
                $"Unsupported persistence provider '{providerValue}'. " +
                "Expected Memory or PostgreSql.");
        }

        var connectionString = configuration.GetConnectionString("Asterloom");
        if (provider == AsterloomPersistenceProvider.PostgreSql
            && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Persistence provider PostgreSql requires ConnectionStrings:Asterloom.");
        }

        return new AsterloomPersistenceOptions(provider, connectionString);
    }
}
