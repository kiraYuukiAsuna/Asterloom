using Asterloom.Modules.Targeting;
using Asterloom.Protocol.Config.Admin.V1;
using Grpc.Core;
using ProtocolDiff = Asterloom.Protocol.Config.V1.ConfigDiff;
using ProtocolEffectiveValue = Asterloom.Protocol.Config.V1.ConfigEffectiveValue;
using ProtocolEntry = Asterloom.Protocol.Config.V1.ConfigEntry;
using ProtocolValidation = Asterloom.Protocol.Config.V1.ConfigValidationResult;

namespace Asterloom.Modules.Config;

public sealed class ConfigAdminGrpcService(ConfigManagementService managementService)
    : ConfigAdminService.ConfigAdminServiceBase
{
    public override async Task<ListConfigEntriesResponse> ListConfigEntries(
        ListConfigEntriesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListEntriesAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListConfigEntriesResponse { NextPageToken = result.NextPageToken };
        response.Entries.AddRange(result.Items.Select(ConfigProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolEntry> GetConfigEntry(
        GetConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.GetEntryAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEntry> CreateConfigEntry(
        CreateConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.CreateEntryAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            request.ValueKind.ToDomain(),
            request.Visibility.ToDomain(),
            request.Definition.ToDomain(),
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEntry> UpdateConfigDraft(
        UpdateConfigDraftRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.DisplayName,
            request.Description,
            request.Visibility.ToDomain(),
            request.Definition.ToDomain(),
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolValidation> ValidateConfigDraft(
        ValidateConfigDraftRequest request,
        ServerCallContext context) =>
        (await managementService.ValidateDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolDiff> DiffConfigDraft(
        DiffConfigDraftRequest request,
        ServerCallContext context) =>
        (await managementService.DiffDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEntry> PublishConfigEntry(
        PublishConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.PublishAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListConfigRevisionsResponse> ListConfigRevisions(
        ListConfigRevisionsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListRevisionsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.PageSize,
            request.PageToken,
            context.CancellationToken);
        var response = new ListConfigRevisionsResponse { NextPageToken = result.NextPageToken };
        response.Revisions.AddRange(result.Items.Select(ConfigProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolEntry> RollbackConfigEntry(
        RollbackConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.RollbackAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.Revision,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEntry> ArchiveConfigEntry(
        ArchiveConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEntry> RestoreConfigEntry(
        RestoreConfigEntryRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.ExpectedVersion,
            context.GetHttpContext().TraceIdentifier,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEffectiveValue> PreviewConfigValue(
        PreviewConfigValueRequest request,
        ServerCallContext context)
    {
        var applicationId = ConfigRuntimeService.ParseId(request.ApplicationId, "applicationId");
        var environmentId = ConfigRuntimeService.ParseId(request.EnvironmentId, "environmentId");
        return (await managementService.PreviewAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.EntryId,
            request.UseDraft,
            request.Context.ToDomain(applicationId, environmentId),
            context.CancellationToken)).ToProtocol();
    }

    public override async Task<ListConfigSnapshotsResponse> ListConfigSnapshots(
        ListConfigSnapshotsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListSnapshotsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            context.CancellationToken);
        var response = new ListConfigSnapshotsResponse { NextPageToken = result.NextPageToken };
        response.Snapshots.AddRange(result.Items.Select(ConfigProtocolMapper.ToProtocol));
        return response;
    }
}
