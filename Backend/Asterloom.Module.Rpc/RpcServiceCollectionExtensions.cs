using Asterloom.Modules.Requests;
using Asterloom.Modules.Rpc.Auditing;
using Asterloom.Modules.Rpc.Errors;
using Asterloom.Modules.Rpc.Requests;
using Google.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.RequestDecompression;

namespace Asterloom.Modules.Rpc;

public static class RpcServiceCollectionExtensions
{
    public static IServiceCollection AddAsterloomRpc(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAsterloomRequestContextAccessor, HttpRequestContextAccessor>();
        services.TryAddSingleton<AsterloomExceptionInterceptor>();
        services.TryAddSingleton<AuditInterceptor>();
        services.AddRequestDecompression();

        services
            .AddGrpc(options =>
            {
                options.EnableDetailedErrors = false;
                options.MaxReceiveMessageSize = 4 * 1024 * 1024;
                options.MaxSendMessageSize = 4 * 1024 * 1024;
                options.Interceptors.Add<AsterloomExceptionInterceptor>();
                options.Interceptors.Add<AuditInterceptor>();
            })
            .AddJsonTranscoding(options =>
            {
                options.TypeRegistry = StandardErrorTypeRegistry.Registry;
            });

        services.AddGrpcSwagger();
        services.AddSwaggerGen();

        return services;
    }
}
