using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;

namespace Asterloom.Modules.Infrastructure.Platform;

internal sealed class InMemoryPlatformResourceStore : IPlatformResourceStore
{
    private readonly Dictionary<Guid, PlatformTenant> _tenants = [];
    private readonly Dictionary<Guid, PlatformApplication> _applications = [];
    private readonly Dictionary<Guid, PlatformEnvironment> _environments = [];
    private readonly Dictionary<(Guid TenantId, Guid ActorId), PlatformTenantMembership>
        _memberships = [];
    private readonly Lock _gate = new();

    public Task<PlatformPage<PlatformTenant>> ListTenantsAsync(
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _tenants.Values.Where(tenant =>
                    IsVisible(tenant.Status, request.IncludeInactive)
                    && Matches(tenant.Slug, tenant.DisplayName, request.Query)),
                request,
                static tenant => tenant.DisplayName,
                static tenant => tenant.Id));
        }
    }

    public Task<PlatformTenant?> GetTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_tenants.GetValueOrDefault(tenantId));
        }
    }

    public Task<bool> TryCreateTenantAsync(
        PlatformTenant tenant,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_tenants.ContainsKey(tenant.Id)
                || _tenants.Values.Any(existing => string.Equals(
                    existing.Slug,
                    tenant.Slug,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _tenants.Add(tenant.Id, tenant);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateTenantAsync(
        PlatformTenant tenant,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_tenants.TryGetValue(tenant.Id, out var current)
                || current.Version != expectedVersion
                || !string.Equals(current.Slug, tenant.Slug, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _tenants[tenant.Id] = tenant;
            return Task.FromResult(true);
        }
    }

    public Task<PlatformPage<PlatformApplication>> ListApplicationsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _applications.Values.Where(application =>
                    application.TenantId == tenantId
                    && IsVisible(application.Status, request.IncludeInactive)
                    && Matches(application.Slug, application.DisplayName, request.Query)),
                request,
                static application => application.DisplayName,
                static application => application.Id));
        }
    }

    public Task<PlatformApplication?> GetApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _applications.TryGetValue(applicationId, out var application)
                    && application.TenantId == tenantId
                    ? application
                    : null);
        }
    }

    public Task<bool> TryCreateApplicationAsync(
        PlatformApplication application,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_applications.ContainsKey(application.Id)
                || _applications.Values.Any(existing =>
                    existing.TenantId == application.TenantId
                    && string.Equals(
                        existing.Slug,
                        application.Slug,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _applications.Add(application.Id, application);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateApplicationAsync(
        PlatformApplication application,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_applications.TryGetValue(application.Id, out var current)
                || current.TenantId != application.TenantId
                || current.Version != expectedVersion
                || !string.Equals(current.Slug, application.Slug, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _applications[application.Id] = application;
            return Task.FromResult(true);
        }
    }

    public Task<PlatformPage<PlatformEnvironment>> ListEnvironmentsAsync(
        Guid tenantId,
        Guid applicationId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _environments.Values.Where(environment =>
                    environment.TenantId == tenantId
                    && environment.ApplicationId == applicationId
                    && IsVisible(environment.Status, request.IncludeInactive)
                    && Matches(environment.Slug, environment.DisplayName, request.Query)),
                request,
                static environment => environment.DisplayName,
                static environment => environment.Id));
        }
    }

    public Task<PlatformEnvironment?> GetEnvironmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _environments.TryGetValue(environmentId, out var environment)
                    && environment.TenantId == tenantId
                    && environment.ApplicationId == applicationId
                    ? environment
                    : null);
        }
    }

    public Task<bool> TryCreateEnvironmentAsync(
        PlatformEnvironment environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_environments.ContainsKey(environment.Id)
                || _environments.Values.Any(existing =>
                    existing.ApplicationId == environment.ApplicationId
                    && string.Equals(
                        existing.Slug,
                        environment.Slug,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            _environments.Add(environment.Id, environment);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateEnvironmentAsync(
        PlatformEnvironment environment,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_environments.TryGetValue(environment.Id, out var current)
                || current.TenantId != environment.TenantId
                || current.ApplicationId != environment.ApplicationId
                || current.Version != expectedVersion
                || !string.Equals(current.Slug, environment.Slug, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _environments[environment.Id] = environment;
            return Task.FromResult(true);
        }
    }

    public Task<PlatformPage<PlatformTenantMembership>> ListTenantMembershipsAsync(
        Guid tenantId,
        PlatformPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(Page(
                _memberships.Values.Where(membership =>
                    membership.TenantId == tenantId
                    && (request.IncludeInactive
                        || membership.Status == PlatformMembershipStatus.Active)),
                request,
                static membership => membership.ActorId.ToString("D"),
                static membership => membership.ActorId));
        }
    }

    public Task<PlatformTenantMembership?> GetTenantMembershipAsync(
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_memberships.GetValueOrDefault((tenantId, actorId)));
        }
    }

    public Task<bool> TryCreateTenantMembershipAsync(
        PlatformTenantMembership membership,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _memberships.TryAdd((membership.TenantId, membership.ActorId), membership));
        }
    }

    public Task<bool> TryUpdateTenantMembershipAsync(
        PlatformTenantMembership membership,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (membership.TenantId, membership.ActorId);
            if (!_memberships.TryGetValue(key, out var current)
                || current.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            _memberships[key] = membership;
            return Task.FromResult(true);
        }
    }

    private static PlatformPage<T> Page<T, TId>(
        IEnumerable<T> source,
        PlatformPageRequest request,
        Func<T, string> displayName,
        Func<T, TId> id)
    {
        var page = source
            .OrderBy(displayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id)
            .Skip(request.Offset)
            .Take(request.Limit + 1)
            .ToArray();
        var hasMore = page.Length > request.Limit;
        return new PlatformPage<T>(
            hasMore ? page[..request.Limit] : page,
            hasMore);
    }

    private static bool IsVisible(PlatformResourceStatus status, bool includeInactive) =>
        includeInactive || status == PlatformResourceStatus.Active;

    private static bool Matches(string slug, string displayName, string? query) =>
        query is null
        || slug.Contains(query, StringComparison.OrdinalIgnoreCase)
        || displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
