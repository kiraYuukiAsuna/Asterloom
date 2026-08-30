using Asterloom.Protocol.Operations.Admin.V1;
using Asterloom.Protocol.Operations.V1;
using Grpc.Core;

namespace Asterloom.Modules.Rpc.Operations;

internal sealed class OperationsAdminGrpcService(
    OperationsMetadataService metadataService)
    : OperationsAdminService.OperationsAdminServiceBase
{
    public override Task<ListApisResponse> ListApis(
        ListApisRequest request,
        ServerCallContext context)
    {
        var response = new ListApisResponse();
        response.Apis.AddRange(OperationsMetadataService.ListApis(request.Query, request.Category));
        return Task.FromResult(response);
    }

    public override Task<OperationsHealth> GetOperationsHealth(
        GetOperationsHealthRequest request,
        ServerCallContext context) =>
        metadataService.GetHealthAsync(context.CancellationToken);

    public override Task<OpenApiDocument> GetOpenApiDocument(
        GetOpenApiDocumentRequest request,
        ServerCallContext context) =>
        Task.FromResult(metadataService.GetOpenApiDocument());
}
