using Asterloom.Protocol.Release.Admin.V1;
using Asterloom.Modules.Targeting;
using Grpc.Core;
using ProtocolArtifact = Asterloom.Protocol.Release.V1.ReleaseArtifact;
using ProtocolChannel = Asterloom.Protocol.Release.V1.ReleaseChannel;
using ProtocolDecision = Asterloom.Protocol.Release.V1.UpdateDecision;
using ProtocolManifest = Asterloom.Protocol.Release.V1.ReleaseManifest;
using ProtocolRelease = Asterloom.Protocol.Release.V1.DesktopRelease;
using ProtocolSigningKey = Asterloom.Protocol.Release.V1.ReleaseSigningKey;
using ProtocolUpload = Asterloom.Protocol.Release.V1.ArtifactUpload;
using ProtocolValidation = Asterloom.Protocol.Release.V1.ReleaseValidationResult;

namespace Asterloom.Modules.Release;

public sealed class ReleaseAdminGrpcService(
    ReleaseManagementService managementService,
    ReleaseEvaluationService evaluationService)
    : ReleaseAdminService.ReleaseAdminServiceBase
{
    public override async Task<ListSigningKeysResponse> ListSigningKeys(
        ListSigningKeysRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListSigningKeysAsync(
            request.TenantId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListSigningKeysResponse { NextPageToken = result.NextPageToken };
        response.SigningKeys.AddRange(result.Items.Select(ReleaseProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolSigningKey> CreateSigningKey(
        CreateSigningKeyRequest request,
        ServerCallContext context) =>
        (await managementService.CreateSigningKeyAsync(
            request.TenantId,
            request.Key,
            request.DisplayName,
            request.PublicKeyPem,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSigningKey> ArchiveSigningKey(
        ArchiveSigningKeyRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveSigningKeyAsync(
            request.TenantId,
            request.SigningKeyId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolSigningKey> RestoreSigningKey(
        RestoreSigningKeyRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreSigningKeyAsync(
            request.TenantId,
            request.SigningKeyId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListChannelsResponse> ListChannels(
        ListChannelsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListChannelsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListChannelsResponse { NextPageToken = result.NextPageToken };
        response.Channels.AddRange(result.Items.Select(ReleaseProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolChannel> GetChannel(
        GetChannelRequest request,
        ServerCallContext context) =>
        (await managementService.GetChannelAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ChannelId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolChannel> CreateChannel(
        CreateChannelRequest request,
        ServerCallContext context) =>
        (await managementService.CreateChannelAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.Key,
            request.DisplayName,
            request.Description,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolChannel> UpdateChannel(
        UpdateChannelRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateChannelAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ChannelId,
            request.DisplayName,
            request.Description,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolChannel> ArchiveChannel(
        ArchiveChannelRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveChannelAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ChannelId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolChannel> RestoreChannel(
        RestoreChannelRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreChannelAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ChannelId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListArtifactsResponse> ListArtifacts(
        ListArtifactsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListArtifactsAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListArtifactsResponse { NextPageToken = result.NextPageToken };
        response.Artifacts.AddRange(result.Items.Select(ReleaseProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolArtifact> GetArtifact(
        GetArtifactRequest request,
        ServerCallContext context) =>
        (await managementService.GetArtifactAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ArtifactId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolUpload> CreateArtifactUpload(
        CreateArtifactUploadRequest request,
        ServerCallContext context) =>
        (await managementService.CreateArtifactUploadAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseVersion,
            request.TargetRuntimeId,
            request.ArtifactKind.ToDomain(),
            request.DeltaFromVersion,
            request.FileName,
            request.ContentType,
            request.SizeBytes,
            request.Sha256,
            request.SigningKeyId,
            request.Signature,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolArtifact> CompleteArtifactUpload(
        CompleteArtifactUploadRequest request,
        ServerCallContext context) =>
        (await managementService.CompleteArtifactUploadAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ArtifactId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolArtifact> ArchiveArtifact(
        ArchiveArtifactRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveArtifactAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ArtifactId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListReleasesResponse> ListReleases(
        ListReleasesRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListReleasesAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeInactive,
            context.CancellationToken);
        var response = new ListReleasesResponse { NextPageToken = result.NextPageToken };
        response.Releases.AddRange(result.Items.Select(ReleaseProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolRelease> GetRelease(
        GetReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.GetReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> CreateRelease(
        CreateReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.CreateReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ChannelId,
            request.ReleaseVersion,
            request.DisplayName,
            request.ReleaseNotes,
            request.ArtifactIds,
            request.RolloutBasisPoints,
            request.TargetSegmentId,
            request.Mandatory,
            request.MinimumVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> UpdateReleaseDraft(
        UpdateReleaseDraftRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateReleaseDraftAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            request.DisplayName,
            request.ReleaseNotes,
            request.ArtifactIds,
            request.RolloutBasisPoints,
            request.TargetSegmentId,
            request.Mandatory,
            request.MinimumVersion,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolValidation> ValidateRelease(
        ValidateReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.ValidateReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> PublishRelease(
        PublishReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.PublishReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            request.ManifestSigningKeyId,
            request.ManifestSignature,
            request.ExpectedVersion,
            request.ExpectedChannelVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> PauseRelease(
        PauseReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.PauseReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> PromoteRelease(
        PromoteReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.PromoteReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            request.RolloutBasisPoints,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolRelease> RollbackRelease(
        RollbackReleaseRequest request,
        ServerCallContext context) =>
        (await managementService.RollbackReleaseAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            request.TargetReleaseId,
            request.ExpectedVersion,
            request.ExpectedTargetVersion,
            request.ExpectedChannelVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolManifest> GetReleaseManifest(
        GetReleaseManifestRequest request,
        ServerCallContext context) =>
        (await managementService.GetReleaseManifestAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ReleaseId,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolDecision> SimulateUpdate(
        SimulateUpdateRequest request,
        ServerCallContext context)
    {
        var scope = ReleaseProtocolMapper.ToReleaseScope(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId);
        return (await evaluationService.CheckForUpdateAsync(
            new(
                scope,
                request.ChannelKey,
                request.CurrentVersion,
                request.TargetRuntimeId,
                request.Context.ToDomain(scope.ApplicationId, scope.EnvironmentId)),
            context.CancellationToken)).ToProtocol();
    }
}
