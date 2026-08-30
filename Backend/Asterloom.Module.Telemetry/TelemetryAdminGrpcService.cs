using Asterloom.Protocol.Telemetry.Admin.V1;
using Asterloom.Protocol.Telemetry.V1;
using Grpc.Core;
using ProtocolSource = Asterloom.Protocol.Telemetry.V1.TelemetrySource;

namespace Asterloom.Modules.Telemetry;

internal sealed class TelemetryAdminGrpcService(
    TelemetryManagementService managementService)
    : TelemetryAdminService.TelemetryAdminServiceBase
{
    public override async Task<ListSourcesResponse> ListSources(
        ListSourcesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListSourcesAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListSourcesResponse { NextPageToken = result.NextPageToken };
        response.Sources.AddRange(result.Items.Select(TelemetryProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSource> GetSource(
        GetSourceRequest request,
        ServerCallContext context) =>
        (await managementService.GetSourceAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SourceId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSource> CreateSource(
        CreateSourceRequest request,
        ServerCallContext context) =>
        (await managementService.CreateSourceAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.ServiceName,
            request.ResourceAttributesJson,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSource> UpdateSource(
        UpdateSourceRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateSourceAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SourceId,
            request.DisplayName,
            request.Description,
            request.ServiceName,
            request.ResourceAttributesJson,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSource> ArchiveSource(
        ArchiveSourceRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveSourceAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SourceId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSource> RestoreSource(
        RestoreSourceRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreSourceAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SourceId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<TelemetrySettings> GetTelemetrySettings(
        GetTelemetrySettingsRequest request,
        ServerCallContext context) =>
        (await managementService.GetSettingsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            context.CancellationToken)).ToProtocol();

    public override async Task<TelemetrySettings> UpdateTelemetrySettings(
        UpdateTelemetrySettingsRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateSettingsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.SamplingRatio,
            request.TracesEnabled,
            request.MetricsEnabled,
            request.LogsEnabled,
            request.ExporterEndpoint,
            request.ExporterProtocol.ToDomain(),
            request.DiagnosticsBaseUrl,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<CollectorHealth> GetCollectorHealth(
        GetCollectorHealthRequest request,
        ServerCallContext context) =>
        (await managementService.GetCollectorHealthAsync(context.CancellationToken)).ToProtocol();

    public override async Task<ListRecentErrorsResponse> ListRecentErrors(
        ListRecentErrorsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListRecentErrorsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.ServiceName,
            request.TraceId,
            context.CancellationToken);
        var response = new ListRecentErrorsResponse { NextPageToken = result.NextPageToken };
        response.Errors.AddRange(result.Items.Select(TelemetryProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<DiagnosticLink> GetDiagnosticLink(
        GetDiagnosticLinkRequest request,
        ServerCallContext context) =>
        (await managementService.GetDiagnosticLinkAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.TraceId,
            request.FromAt?.ToDateTimeOffset(),
            request.ToAt?.ToDateTimeOffset(),
            context.CancellationToken)).ToProtocol();
}
