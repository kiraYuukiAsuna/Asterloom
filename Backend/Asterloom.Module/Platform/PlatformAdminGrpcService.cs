using Asterloom.Modules.Platform.Model;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ProtocolApplication = Asterloom.Protocol.Platform.Admin.V1.Application;
using ProtocolEnvironment = Asterloom.Protocol.Platform.Admin.V1.Environment;

namespace Asterloom.Modules.Platform;

public sealed class PlatformAdminGrpcService(
    PlatformInfoProvider platformInfoProvider,
    PlatformManagementService managementService)
    : PlatformAdminService.PlatformAdminServiceBase
{
    public override Task<GetPlatformInfoResponse> GetPlatformInfo(
        Empty request,
        ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(platformInfoProvider.GetPlatformInfo());
    }

    public override async Task<ListTenantsResponse> ListTenants(
        ListTenantsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListTenantsAsync(
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListTenantsResponse { NextPageToken = result.NextPageToken };
        response.Tenants.AddRange(result.Items.Select(PlatformProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<Tenant> CreateTenant(
        CreateTenantRequest request,
        ServerCallContext context) =>
        (await managementService.CreateTenantAsync(
            request.Slug,
            request.DisplayName,
            context.CancellationToken)).ToProtocol();

    public override async Task<Tenant> UpdateTenant(
        UpdateTenantRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateTenantAsync(
            request.TenantId,
            request.DisplayName,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<Tenant> ArchiveTenant(
        ArchiveTenantRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveTenantAsync(
            request.TenantId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<Tenant> RestoreTenant(
        RestoreTenantRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreTenantAsync(
            request.TenantId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListApplicationsResponse> ListApplications(
        ListApplicationsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListApplicationsAsync(
            request.TenantId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListApplicationsResponse { NextPageToken = result.NextPageToken };
        response.Applications.AddRange(
            result.Items.Select(PlatformProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolApplication> CreateApplication(
        CreateApplicationRequest request,
        ServerCallContext context) =>
        (await managementService.CreateApplicationAsync(
            request.TenantId,
            request.Slug,
            request.DisplayName,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolApplication> UpdateApplication(
        UpdateApplicationRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateApplicationAsync(
            request.TenantId,
            request.ApplicationId,
            request.DisplayName,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolApplication> ArchiveApplication(
        ArchiveApplicationRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveApplicationAsync(
            request.TenantId,
            request.ApplicationId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolApplication> RestoreApplication(
        RestoreApplicationRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreApplicationAsync(
            request.TenantId,
            request.ApplicationId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListEnvironmentsResponse> ListEnvironments(
        ListEnvironmentsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListEnvironmentsAsync(
            request.TenantId,
            request.ApplicationId,
            request.PageSize,
            request.PageToken,
            request.Query,
            request.IncludeArchived,
            context.CancellationToken);
        var response = new ListEnvironmentsResponse { NextPageToken = result.NextPageToken };
        response.Environments.AddRange(
            result.Items.Select(PlatformProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<ProtocolEnvironment> CreateEnvironment(
        CreateEnvironmentRequest request,
        ServerCallContext context) =>
        (await managementService.CreateEnvironmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.Slug,
            request.DisplayName,
            ToDomain(request.EnvironmentType),
            request.IsProtected,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEnvironment> UpdateEnvironment(
        UpdateEnvironmentRequest request,
        ServerCallContext context) =>
        (await managementService.UpdateEnvironmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.DisplayName,
            ToDomain(request.EnvironmentType),
            request.IsProtected,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEnvironment> ArchiveEnvironment(
        ArchiveEnvironmentRequest request,
        ServerCallContext context) =>
        (await managementService.ArchiveEnvironmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ProtocolEnvironment> RestoreEnvironment(
        RestoreEnvironmentRequest request,
        ServerCallContext context) =>
        (await managementService.RestoreEnvironmentAsync(
            request.TenantId,
            request.ApplicationId,
            request.EnvironmentId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<ListTenantMembershipsResponse> ListTenantMemberships(
        ListTenantMembershipsRequest request,
        ServerCallContext context)
    {
        var result = await managementService.ListTenantMembershipsAsync(
            request.TenantId,
            request.PageSize,
            request.PageToken,
            request.IncludeRemoved,
            context.CancellationToken);
        var response = new ListTenantMembershipsResponse
        {
            NextPageToken = result.NextPageToken,
        };
        response.Memberships.AddRange(
            result.Items.Select(PlatformProtocolMapper.ToProtocol));
        return response;
    }

    public override async Task<TenantMembership> SetTenantMembership(
        SetTenantMembershipRequest request,
        ServerCallContext context) =>
        (await managementService.SetTenantMembershipAsync(
            request.TenantId,
            request.ActorId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    public override async Task<TenantMembership> RemoveTenantMembership(
        RemoveTenantMembershipRequest request,
        ServerCallContext context) =>
        (await managementService.RemoveTenantMembershipAsync(
            request.TenantId,
            request.ActorId,
            request.ExpectedVersion,
            context.CancellationToken)).ToProtocol();

    private static PlatformEnvironmentType ToDomain(EnvironmentType environmentType) =>
        environmentType switch
        {
            EnvironmentType.Development => PlatformEnvironmentType.Development,
            EnvironmentType.Staging => PlatformEnvironmentType.Staging,
            EnvironmentType.Production => PlatformEnvironmentType.Production,
            _ => (PlatformEnvironmentType)0,
        };
}
