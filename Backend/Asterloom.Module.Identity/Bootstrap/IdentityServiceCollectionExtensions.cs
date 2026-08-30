using Asterloom.Modules.Identity.Model;
using Asterloom.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Identity.Bootstrap;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddAsterloomIdentityCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAsterloomIdentityPersistence(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(IdentityBootstrapOptions.FromConfiguration(configuration));
        services.AddScoped<IIdentityBootstrapper, IdentityBootstrapper>();
        services
            .AddIdentity<AsterloomUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddEntityFrameworkStores<AsterloomIdentityDbContext>()
            .AddDefaultTokenProviders();
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<AsterloomIdentityDbContext>();
            });
        return services;
    }
}
