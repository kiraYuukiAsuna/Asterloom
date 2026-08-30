using Asterloom.Modules.Targeting;
using Asterloom.Protocol.Config.V1;
using Grpc.Core;

namespace Asterloom.Modules.Config;

public sealed class ConfigGrpcService(ConfigRuntimeService runtimeService)
    : ConfigService.ConfigServiceBase
{
    public override Task<ConfigSnapshotResponse> GetConfigSnapshot(
        ConfigSnapshotRequest request,
        ServerCallContext context) =>
        GetSnapshotAsync(request, includeServerValues: false, context);

    public override Task<ConfigSnapshotResponse> GetServerConfigSnapshot(
        ConfigSnapshotRequest request,
        ServerCallContext context) =>
        GetSnapshotAsync(request, includeServerValues: true, context);

    public override async Task<ConfigUpdateStatus> CheckConfigUpdates(
        ConfigUpdateCheckRequest request,
        ServerCallContext context)
    {
        var applicationId = ConfigRuntimeService.ParseId(request.ApplicationId, "applicationId");
        var environmentId = ConfigRuntimeService.ParseId(request.EnvironmentId, "environmentId");
        var result = await runtimeService.CheckUpdatesAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.KnownSnapshotVersion,
            request.Context.ToDomain(applicationId, environmentId),
            context.CancellationToken);
        context.GetHttpContext().Response.Headers.ETag = result.ETag;
        return result.ToProtocol();
    }

    private async Task<ConfigSnapshotResponse> GetSnapshotAsync(
        ConfigSnapshotRequest request,
        bool includeServerValues,
        ServerCallContext context)
    {
        var applicationId = ConfigRuntimeService.ParseId(request.ApplicationId, "applicationId");
        var environmentId = ConfigRuntimeService.ParseId(request.EnvironmentId, "environmentId");
        var httpContext = context.GetHttpContext();
        var ifNoneMatch = string.IsNullOrWhiteSpace(request.IfNoneMatch)
            ? httpContext.Request.Headers.IfNoneMatch.ToString()
            : request.IfNoneMatch;
        var result = await runtimeService.GetSnapshotAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Context.ToDomain(applicationId, environmentId),
            ifNoneMatch,
            includeServerValues,
            context.CancellationToken);
        httpContext.Response.Headers.ETag = result.ETag;
        httpContext.Response.Headers.CacheControl = "private, no-cache";
        return result.ToProtocol();
    }
}
