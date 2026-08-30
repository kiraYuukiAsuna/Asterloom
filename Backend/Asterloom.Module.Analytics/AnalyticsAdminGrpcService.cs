using Asterloom.Protocol.Analytics.Admin.V1;
using Asterloom.Protocol.Analytics.V1;
using Google.Protobuf;
using Grpc.Core;
using ProtocolEvent = Asterloom.Protocol.Analytics.V1.AnalyticsEvent;
using ProtocolSchema = Asterloom.Protocol.Analytics.V1.EventSchema;
using ProtocolWriteKey = Asterloom.Protocol.Analytics.V1.AnalyticsWriteKey;
using ProtocolWriteKeyCredential = Asterloom.Protocol.Analytics.V1.AnalyticsWriteKeyCredential;

namespace Asterloom.Modules.Analytics;

internal sealed class AnalyticsAdminGrpcService(AnalyticsManagementService managementService)
    : AnalyticsAdminService.AnalyticsAdminServiceBase
{
    public override async Task<ListEventSchemasResponse> ListEventSchemas(
        ListEventSchemasRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListEventSchemasAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListEventSchemasResponse { NextPageToken = result.NextPageToken };
        response.EventSchemas.AddRange(result.Items.Select(AnalyticsProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSchema> GetEventSchema(
        GetEventSchemaRequest request,
        ServerCallContext context) =>
        (await managementService.GetEventSchemaAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventSchemaId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSchema> CreateEventSchema(
        CreateEventSchemaRequest request,
        ServerCallContext context) =>
        (await managementService.CreateEventSchemaAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.SchemaJson,
            request.RetentionDays,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSchema> UpdateEventSchema(
        UpdateEventSchemaRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateEventSchemaAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventSchemaId,
            request.DisplayName,
            request.Description,
            request.SchemaJson,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSchema> ArchiveEventSchema(
        ArchiveEventSchemaRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveEventSchemaAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventSchemaId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSchema> RestoreEventSchema(
        RestoreEventSchemaRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreEventSchemaAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventSchemaId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListWriteKeysResponse> ListWriteKeys(
        ListWriteKeysRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListWriteKeysAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.IncludeRevoked,
            context.CancellationToken);
        var response = new ListWriteKeysResponse();
        response.WriteKeys.AddRange(result.Select(AnalyticsProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolWriteKeyCredential> CreateWriteKey(
        CreateWriteKeyRequest request,
        ServerCallContext context)
    {
        var result = await managementService.CreateWriteKeyAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Name,
            context.CancellationToken);
        return new ProtocolWriteKeyCredential
        {
            WriteKey = result.WriteKey.ToProtocol(),
            Secret = result.Secret,
        };
    }

    public override async Task<ProtocolWriteKeyCredential> RotateWriteKey(
        RotateWriteKeyRequest request,
        ServerCallContext context)
    {
        var result = await managementService.RotateWriteKeyAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.WriteKeyId,
            request.ExpectedVersion,
            context.CancellationToken);
        return new ProtocolWriteKeyCredential
        {
            WriteKey = result.WriteKey.ToProtocol(),
            Secret = result.Secret,
        };
    }

    public override async Task<ProtocolWriteKey> RevokeWriteKey(
        RevokeWriteKeyRequest request,
        ServerCallContext context) =>
        (await managementService.RevokeWriteKeyAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.WriteKeyId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListEventsResponse> ListEvents(
        ListEventsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListEventsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.EventName,
            request.ActorId,
            request.EventId,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            context.CancellationToken);
        var response = new ListEventsResponse { NextPageToken = result.NextPageToken };
        response.Events.AddRange(result.Items.Select(AnalyticsProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolEvent> GetEvent(
        GetEventRequest request,
        ServerCallContext context) =>
        (await managementService.GetEventAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.AnalyticsEventId,
            context.CancellationToken)).ToProtocol();

    public override async Task<QueryAnalyticsResponse> QueryAnalytics(
        QueryAnalyticsRequest request,
        ServerCallContext context)
    {
        var buckets = await managementService.QueryAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventNames,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            request.Interval,
            context.CancellationToken);
        var response = new QueryAnalyticsResponse();
        response.Buckets.AddRange(buckets.Select(AnalyticsProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSchema> UpdateRetention(
        UpdateRetentionRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateRetentionAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventSchemaId,
            request.RetentionDays,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ExportEventsResponse> ExportEvents(
        ExportEventsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ExportAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EventName,
            request.ActorId,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            request.MaximumRows,
            context.CancellationToken);
        return new ExportEventsResponse
        {
            FileName = result.FileName,
            ContentType = result.ContentType,
            Content = ByteString.CopyFrom(result.Content),
            ExportedRows = result.ExportedRows,
        };
    }
}
