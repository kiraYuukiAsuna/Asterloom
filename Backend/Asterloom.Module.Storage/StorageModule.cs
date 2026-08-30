using Asterloom.Modules.Hosting;
using Asterloom.Modules.Persistence;
using Asterloom.Modules.Security;
using Asterloom.Modules.Storage.Persistence;
using Asterloom.Modules.Storage.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asterloom.Modules.Storage;

public sealed class StorageModule : IAsterloomModule
{
    public string Name => "Storage";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAsterloomModuleMigration, StorageInitialMigration>());
        services.AddScoped<StorageManagementService>();
        services.AddScoped<StorageAdminGrpcService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGrpcService<StorageAdminGrpcService>()
            .RequireAuthorization(AsterloomApiAuthorization.ManagementPolicy);

        endpoints.MapPut(
            "/api/v1/storage/transfers/uploads/{transferId:guid}",
            async Task<IResult> (
                Guid transferId,
                string token,
                HttpRequest request,
                IObjectStorageTransport transport,
                CancellationToken cancellationToken) =>
            {
                var accepted = await transport.TryAcceptLocalUploadAsync(
                    transferId,
                    token,
                    request.Body,
                    request.ContentType,
                    request.ContentLength,
                    cancellationToken);
                return accepted ? Results.NoContent() : Results.NotFound();
            })
            .AllowAnonymous();

        endpoints.MapGet(
            "/api/v1/storage/transfers/downloads/{transferId:guid}",
            async Task<IResult> (
                Guid transferId,
                string token,
                IObjectStorageTransport transport,
                CancellationToken cancellationToken) =>
            {
                var download = await transport.TryOpenLocalDownloadAsync(
                    transferId,
                    token,
                    cancellationToken);
                return download is null
                    ? Results.NotFound()
                    : Results.Stream(
                        download.Content,
                        download.ContentType,
                        download.FileName,
                        enableRangeProcessing: false);
            })
            .AllowAnonymous();
    }
}
