using Asterloom.Protocol.Analytics.V1;
using Grpc.Core;
using ProtocolIngestionFailure = Asterloom.Protocol.Analytics.V1.AnalyticsIngestionFailure;

namespace Asterloom.Modules.Analytics;

internal sealed class AnalyticsGrpcService(AnalyticsIngestionService ingestionService)
    : AnalyticsService.AnalyticsServiceBase
{
    public override async Task<IngestEventsResponse> IngestEvents(
        IngestEventsRequest request,
        ServerCallContext context)
    {
        var secret = context.GetHttpContext().Request.Headers["X-Asterloom-Write-Key"]
            .FirstOrDefault();
        var result = await ingestionService.IngestAsync(
            secret,
            request.Events.Select(static item => new AnalyticsEventEnvelope(
                item.EventId,
                item.EventName,
                item.OccurredAt?.ToDateTimeOffset(),
                item.ActorId,
                item.AnonymousId,
                item.SessionId,
                item.PropertiesJson,
                item.ContextJson,
                item.SdkName,
                item.SdkVersion)).ToArray(),
            context.CancellationToken);
        var response = new IngestEventsResponse
        {
            Accepted = result.Accepted,
            Rejected = result.Rejected,
            Deduplicated = result.Deduplicated,
        };
        response.Failures.AddRange(result.Failures.Select(static failure =>
            new ProtocolIngestionFailure
            {
                EventId = failure.EventId,
                ErrorCode = failure.ErrorCode,
                Message = failure.Message,
            }));
        return response;
    }
}
