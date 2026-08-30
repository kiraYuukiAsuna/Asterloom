using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Identity.Persistence;

public enum IdentityPersistenceProvider
{
    Memory = 0,
    PostgreSql = 1,
}

public sealed record IdentityPersistenceOptions(
    IdentityPersistenceProvider Provider,
    string? ConnectionString,
    string InMemoryDatabaseName)
{
    public static IdentityPersistenceOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredProvider = configuration["Persistence:Provider"] ?? "PostgreSql";
        if (!Enum.TryParse<IdentityPersistenceProvider>(
                configuredProvider,
                ignoreCase: true,
                out var provider))
        {
            throw new InvalidOperationException(
                $"Unsupported Identity persistence provider '{configuredProvider}'. " +
                "Expected Memory or PostgreSql.");
        }

        var connectionString = configuration.GetConnectionString("Asterloom");
        if (provider == IdentityPersistenceProvider.PostgreSql
            && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Identity persistence with PostgreSql requires ConnectionStrings:Asterloom.");
        }

        return new IdentityPersistenceOptions(
            provider,
            connectionString,
            $"asterloom-identity-{Guid.CreateVersion7():N}");
    }
}

public static class IdentityPersistence
{
    public const string Schema = "identity";

    public static IServiceCollection AddAsterloomIdentityPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var persistence = IdentityPersistenceOptions.FromConfiguration(configuration);
        services.TryAddSingleton(persistence);
        services.AddDbContext<AsterloomIdentityDbContext>(options =>
        {
            if (persistence.Provider == IdentityPersistenceProvider.Memory)
            {
                options.UseInMemoryDatabase(persistence.InMemoryDatabaseName);
            }
            else
            {
                options.UseNpgsql(
                    persistence.ConnectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        Schema));
            }

            options.UseOpenIddict();
        });
        services.TryAddScoped<IIdentityDatabaseMigrator, IdentityDatabaseMigrator>();
        return services;
    }
}
