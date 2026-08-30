using Asterloom.Protocol.Audit.Admin.V1;
using Google.Protobuf;
using Grpc.Core;
using ProtocolAuditEvent = Asterloom.Protocol.Audit.V1.AuditEvent;

namespace Asterloom.Modules.Auditing;

internal sealed class AuditAdminGrpcService(AuditManagementService managementService)
    : AuditAdminService.AuditAdminServiceBase
{
    public override async Task<ListAuditEventsResponse> ListAuditEvents(
        ListAuditEventsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListAsync(
            request.PageSize,
            request.PageToken,
            request.ActorId,
            request.Operation,
            request.Outcome.ToDomain(),
            request.RequestId,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            context.CancellationToken);
        var response = new ListAuditEventsResponse { NextPageToken = result.NextPageToken };
        response.AuditEvents.AddRange(result.Items.Select(AuditProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolAuditEvent> GetAuditEvent(
        GetAuditEventRequest request,
        ServerCallContext context) =>
        (await managementService.GetAsync(
            request.AuditEventId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ExportAuditEventsResponse> ExportAuditEvents(
        ExportAuditEventsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ExportAsync(
            request.ActorId,
            request.Operation,
            request.Outcome.ToDomain(),
            request.RequestId,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            request.MaximumRows,
            context.CancellationToken);
        return new ExportAuditEventsResponse
        {
            FileName = result.FileName,
            ContentType = result.ContentType,
            Content = ByteString.CopyFrom(result.Content),
            ExportedRows = result.ExportedRows,
        };
    }
}
