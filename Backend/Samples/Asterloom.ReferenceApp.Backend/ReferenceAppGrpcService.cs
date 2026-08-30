using Asterloom.ReferenceApp.Protocol.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Asterloom.ReferenceApp.Backend;

internal sealed partial class ReferenceAppGrpcService(
    ReferenceAppStore store,
    ReferenceAppInstrumentation instrumentation,
    ILogger<ReferenceAppGrpcService> logger) : ReferenceAppService.ReferenceAppServiceBase
{
    public override async Task<ClientHeartbeat> RecordHeartbeat(
        RecordHeartbeatRequest request,
        ServerCallContext context)
    {
        var clientInstanceId = RequireText(
            request.ClientInstanceId,
            "client_instance_id",
            200);
        var clientVersion = RequireText(request.ClientVersion, "client_version", 100);
        var platform = RequireText(request.Platform, "platform", 100);
        if (request.Attributes.Count > 50
            || request.Attributes.Any(item => item.Key.Length > 100 || item.Value.Length > 500))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "attributes exceed the supported limits"));
        }

        using var activity = instrumentation.ActivitySource.StartActivity("reference.heartbeat.record");
        activity?.SetTag("client.instance.id", clientInstanceId);
        activity?.SetTag("client.version", clientVersion);
        var item = await store.RecordAsync(
            clientInstanceId,
            clientVersion,
            platform,
            request.Attributes,
            context.CancellationToken);
        instrumentation.Heartbeats.Add(1, new KeyValuePair<string, object?>("platform", platform));
        LogHeartbeatRecorded(logger, item.Id, clientInstanceId);
        return ToProtocol(item);
    }

    public override async Task<ReferenceAppStatus> GetStatus(
        GetStatusRequest request,
        ServerCallContext context)
    {
        var status = await store.GetStatusAsync(context.CancellationToken);
        return new ReferenceAppStatus
        {
            Service = "asterloom.reference.backend",
            Version = typeof(ReferenceAppGrpcService).Assembly.GetName().Version?.ToString()
                ?? "0.0.0",
            PersistenceReady = true,
            HeartbeatCount = status.HeartbeatCount,
            LastHeartbeatAt = status.LastHeartbeatAt is null
                ? null
                : Timestamp.FromDateTimeOffset(status.LastHeartbeatAt.Value),
        };
    }

    public override async Task<ListHeartbeatsResponse> ListHeartbeats(
        ListHeartbeatsRequest request,
        ServerCallContext context)
    {
        var pageSize = request.PageSize == 0 ? 20 : request.PageSize;
        if (pageSize is < 1 or > 100)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "page_size must be between 1 and 100"));
        }

        var response = new ListHeartbeatsResponse();
        response.Heartbeats.AddRange(
            (await store.ListAsync(pageSize, context.CancellationToken)).Select(ToProtocol));
        return response;
    }

    private static ClientHeartbeat ToProtocol(StoredHeartbeat item)
    {
        var result = new ClientHeartbeat
        {
            Id = item.Id.ToString("D"),
            ClientInstanceId = item.ClientInstanceId,
            ClientVersion = item.ClientVersion,
            Platform = item.Platform,
            RecordedAt = Timestamp.FromDateTimeOffset(item.RecordedAt),
        };
        foreach (var attribute in item.Attributes)
        {
            result.Attributes.Add(attribute.Key, attribute.Value);
        }

        return result;
    }

    private static string RequireText(string value, string field, int maximumLength)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maximumLength)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"{field} must contain 1-{maximumLength} characters"));
        }

        return normalized;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Recorded reference heartbeat {HeartbeatId} for client {ClientInstanceId}.")]
    private static partial void LogHeartbeatRecorded(
        ILogger logger,
        Guid heartbeatId,
        string clientInstanceId);
}
