using Asterloom.Modules.Platform.Model;

namespace Asterloom.Modules.Platform.Persistence;

public interface IPlatformResourceStore
{
    Task<PlatformPage<PlatformTenant>> ListTenantsAsync(
        PlatformPageRequest request,
        CancellationToken cancellationToken);

    Task<PlatformTenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<bool> TryCreateTenantAsync(
        PlatformTenant tenant,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateTenantAsync(
        PlatformTenant tenant,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<PlatformPage<PlatformApplication>> ListApplicationsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken);

    Task<PlatformApplication?> GetApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateApplicationAsync(
        PlatformApplication application,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateApplicationAsync(
        PlatformApplication application,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<PlatformPage<PlatformEnvironment>> ListEnvironmentsAsync(
        Guid tenantId,
        Guid applicationId,
        PlatformPageRequest request,
        CancellationToken cancellationToken);

    Task<PlatformEnvironment?> GetEnvironmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateEnvironmentAsync(
        PlatformEnvironment environment,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateEnvironmentAsync(
        PlatformEnvironment environment,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<PlatformPage<PlatformTenantMembership>> ListTenantMembershipsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken);

    Task<PlatformTenantMembership?> GetTenantMembershipAsync(
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateTenantMembershipAsync(
        PlatformTenantMembership membership,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateTenantMembershipAsync(
        PlatformTenantMembership membership,
        long expectedVersion,
        CancellationToken cancellationToken);
}
