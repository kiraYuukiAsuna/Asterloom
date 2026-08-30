using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Platform;

public sealed partial class PlatformManagementService(
    IPlatformResourceStore store,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    public async Task<PlatformListResult<PlatformTenant>> ListTenantsAsync(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListTenantsAsync(page, cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<PlatformTenant> CreateTenantAsync(
        string slug,
        string displayName,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tenant = new PlatformTenant(
            Guid.CreateVersion7(),
            NormalizeSlug(slug),
            NormalizeDisplayName(displayName),
            PlatformResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateTenantAsync(tenant, cancellationToken))
        {
            throw AlreadyExists("tenant_slug_exists", "A tenant with this slug already exists.");
        }

        return tenant;
    }

    public Task<PlatformTenant> UpdateTenantAsync(
        string tenantId,
        string displayName,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeTenantAsync(
            ParseId(tenantId, "tenantId"),
            expectedVersion,
            current =>
            {
                RequireActive(current.Status, "tenant");
                return current with
                {
                    DisplayName = NormalizeDisplayName(displayName),
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                };
            },
            cancellationToken);

    public Task<PlatformTenant> ArchiveTenantAsync(
        string tenantId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeTenantAsync(
            ParseId(tenantId, "tenantId"),
            expectedVersion,
            current => current.Status == PlatformResourceStatus.Archived
                ? current
                : current with
                {
                    Status = PlatformResourceStatus.Archived,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    ArchivedAt = timeProvider.GetUtcNow(),
                },
            cancellationToken);

    public Task<PlatformTenant> RestoreTenantAsync(
        string tenantId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeTenantAsync(
            ParseId(tenantId, "tenantId"),
            expectedVersion,
            current => current.Status == PlatformResourceStatus.Active
                ? current
                : current with
                {
                    Status = PlatformResourceStatus.Active,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    ArchivedAt = null,
                },
            cancellationToken);

    public async Task<PlatformListResult<PlatformApplication>> ListApplicationsAsync(
        string tenantId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(parsedTenantId, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListApplicationsAsync(
            parsedTenantId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<PlatformApplication> CreateApplicationAsync(
        string tenantId,
        string slug,
        string displayName,
        CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        var tenant = await RequireTenantAsync(parsedTenantId, cancellationToken);
        RequireActive(tenant.Status, "tenant");
        var now = timeProvider.GetUtcNow();
        var application = new PlatformApplication(
            Guid.CreateVersion7(),
            parsedTenantId,
            NormalizeSlug(slug),
            NormalizeDisplayName(displayName),
            PlatformResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateApplicationAsync(application, cancellationToken))
        {
            throw AlreadyExists(
                "application_slug_exists",
                "An application with this slug already exists in the tenant.");
        }

        return application;
    }

    public Task<PlatformApplication> UpdateApplicationAsync(
        string tenantId,
        string applicationId,
        string displayName,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeApplicationAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            expectedVersion,
            current =>
            {
                RequireActive(current.Status, "application");
                return current with
                {
                    DisplayName = NormalizeDisplayName(displayName),
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                };
            },
            cancellationToken);

    public Task<PlatformApplication> ArchiveApplicationAsync(
        string tenantId,
        string applicationId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeApplicationAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            expectedVersion,
            current => current.Status == PlatformResourceStatus.Archived
                ? current
                : current with
                {
                    Status = PlatformResourceStatus.Archived,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    ArchivedAt = timeProvider.GetUtcNow(),
                },
            cancellationToken);

    public Task<PlatformApplication> RestoreApplicationAsync(
        string tenantId,
        string applicationId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeApplicationAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            expectedVersion,
            current => current.Status == PlatformResourceStatus.Active
                ? current
                : current with
                {
                    Status = PlatformResourceStatus.Active,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    ArchivedAt = null,
                },
            cancellationToken);

    public async Task<PlatformListResult<PlatformEnvironment>> ListEnvironmentsAsync(
        string tenantId,
        string applicationId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        var parsedApplicationId = ParseId(applicationId, "applicationId");
        await RequireApplicationAsync(parsedTenantId, parsedApplicationId, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListEnvironmentsAsync(
            parsedTenantId,
            parsedApplicationId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<PlatformEnvironment> CreateEnvironmentAsync(
        string tenantId,
        string applicationId,
        string slug,
        string displayName,
        PlatformEnvironmentType environmentType,
        bool isProtected,
        CancellationToken cancellationToken)
    {
        ValidateEnvironmentType(environmentType);
        var parsedTenantId = ParseId(tenantId, "tenantId");
        var parsedApplicationId = ParseId(applicationId, "applicationId");
        var tenant = await RequireTenantAsync(parsedTenantId, cancellationToken);
        RequireActive(tenant.Status, "tenant");
        var application = await RequireApplicationAsync(
            parsedTenantId,
            parsedApplicationId,
            cancellationToken);
        RequireActive(application.Status, "application");
        var now = timeProvider.GetUtcNow();
        var environment = new PlatformEnvironment(
            Guid.CreateVersion7(),
            parsedTenantId,
            parsedApplicationId,
            NormalizeSlug(slug),
            NormalizeDisplayName(displayName),
            environmentType,
            isProtected,
            PlatformResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateEnvironmentAsync(environment, cancellationToken))
        {
            throw AlreadyExists(
                "environment_slug_exists",
                "An environment with this slug already exists in the application.");
        }

        return environment;
    }

    public Task<PlatformEnvironment> UpdateEnvironmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string displayName,
        PlatformEnvironmentType environmentType,
        bool isProtected,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ValidateEnvironmentType(environmentType);
        return ChangeEnvironmentAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"),
            expectedVersion,
            current =>
            {
                RequireActive(current.Status, "environment");
                return current with
                {
                    DisplayName = NormalizeDisplayName(displayName),
                    EnvironmentType = environmentType,
                    IsProtected = isProtected,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                };
            },
            cancellationToken);
    }

    public Task<PlatformEnvironment> ArchiveEnvironmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeEnvironmentAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"),
            expectedVersion,
            current =>
            {
                if (current.Status == PlatformResourceStatus.Archived)
                {
                    return current;
                }

                if (current.IsProtected)
                {
                    throw FailedPrecondition(
                        "environment_protected",
                        "Unprotect the environment before archiving it.");
                }

                var now = timeProvider.GetUtcNow();
                return current with
                {
                    Status = PlatformResourceStatus.Archived,
                    Version = current.Version + 1,
                    UpdatedAt = now,
                    ArchivedAt = now,
                };
            },
            cancellationToken);

    public Task<PlatformEnvironment> RestoreEnvironmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeEnvironmentAsync(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"),
            expectedVersion,
            current => current.Status == PlatformResourceStatus.Active
                ? current
                : current with
                {
                    Status = PlatformResourceStatus.Active,
                    Version = current.Version + 1,
                    UpdatedAt = timeProvider.GetUtcNow(),
                    ArchivedAt = null,
                },
            cancellationToken);

    public async Task<PlatformListResult<PlatformTenantMembership>>
        ListTenantMembershipsAsync(
            string tenantId,
            int pageSize,
            string? pageToken,
            bool includeRemoved,
            CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        await RequireTenantAsync(parsedTenantId, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeRemoved);
        var result = await store.ListTenantMembershipsAsync(
            parsedTenantId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<PlatformTenantMembership> SetTenantMembershipAsync(
        string tenantId,
        string actorId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        var parsedActorId = ParseId(actorId, "actorId");
        var tenant = await RequireTenantAsync(parsedTenantId, cancellationToken);
        RequireActive(tenant.Status, "tenant");
        var current = await store.GetTenantMembershipAsync(
            parsedTenantId,
            parsedActorId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (current is null)
        {
            if (expectedVersion != 0)
            {
                throw NotFound("tenant_membership_not_found", "The tenant membership was not found.");
            }

            var created = new PlatformTenantMembership(
                parsedTenantId,
                parsedActorId,
                PlatformMembershipStatus.Active,
                Version: 1,
                now,
                now);
            if (!await store.TryCreateTenantMembershipAsync(created, cancellationToken))
            {
                throw VersionConflict();
            }

            return created;
        }

        RequireVersion(current.Version, expectedVersion);
        if (current.Status == PlatformMembershipStatus.Active)
        {
            return current;
        }

        var updated = current with
        {
            Status = PlatformMembershipStatus.Active,
            Version = current.Version + 1,
            UpdatedAt = now,
        };
        if (!await store.TryUpdateTenantMembershipAsync(
                updated,
                current.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<PlatformTenantMembership> RemoveTenantMembershipAsync(
        string tenantId,
        string actorId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var parsedTenantId = ParseId(tenantId, "tenantId");
        var parsedActorId = ParseId(actorId, "actorId");
        var current = await store.GetTenantMembershipAsync(
            parsedTenantId,
            parsedActorId,
            cancellationToken)
            ?? throw NotFound(
                "tenant_membership_not_found",
                "The tenant membership was not found.");
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == PlatformMembershipStatus.Removed)
        {
            return current;
        }

        var updated = current with
        {
            Status = PlatformMembershipStatus.Removed,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateTenantMembershipAsync(
                updated,
                current.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<PlatformTenant> ChangeTenantAsync(
        Guid tenantId,
        long expectedVersion,
        Func<PlatformTenant, PlatformTenant> change,
        CancellationToken cancellationToken)
    {
        var current = await RequireTenantAsync(tenantId, cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        var updated = change(current);
        if (ReferenceEquals(updated, current) || updated == current)
        {
            return current;
        }

        if (!await store.TryUpdateTenantAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<PlatformApplication> ChangeApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        long expectedVersion,
        Func<PlatformApplication, PlatformApplication> change,
        CancellationToken cancellationToken)
    {
        var current = await RequireApplicationAsync(
            tenantId,
            applicationId,
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        var updated = change(current);
        if (ReferenceEquals(updated, current) || updated == current)
        {
            return current;
        }

        if (!await store.TryUpdateApplicationAsync(
                updated,
                current.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<PlatformEnvironment> ChangeEnvironmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        long expectedVersion,
        Func<PlatformEnvironment, PlatformEnvironment> change,
        CancellationToken cancellationToken)
    {
        var current = await store.GetEnvironmentAsync(
            tenantId,
            applicationId,
            environmentId,
            cancellationToken)
            ?? throw NotFound("environment_not_found", "The environment was not found.");
        RequireVersion(current.Version, expectedVersion);
        var updated = change(current);
        if (ReferenceEquals(updated, current) || updated == current)
        {
            return current;
        }

        if (!await store.TryUpdateEnvironmentAsync(
                updated,
                current.Version,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<PlatformTenant> RequireTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await store.GetTenantAsync(tenantId, cancellationToken)
            ?? throw NotFound("tenant_not_found", "The tenant was not found.");

    private async Task<PlatformApplication> RequireApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        CancellationToken cancellationToken) =>
        await store.GetApplicationAsync(tenantId, applicationId, cancellationToken)
            ?? throw NotFound("application_not_found", "The application was not found.");

    private static PlatformPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive)
    {
        if (pageSize < 0 || pageSize > MaximumPageSize)
        {
            throw Invalid(
                "pageSize",
                $"Page size must be between 0 and {MaximumPageSize}.");
        }

        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (normalizedQuery?.Length > 100)
        {
            throw Invalid("query", "Search query cannot exceed 100 characters.");
        }

        return new PlatformPageRequest(
            DecodePageToken(pageToken),
            pageSize == 0 ? DefaultPageSize : pageSize,
            normalizedQuery,
            includeInactive);
    }

    private static PlatformListResult<T> ToListResult<T>(PlatformPage<T> page, int offset) =>
        new(
            page.Items,
            page.HasMore ? EncodePageToken(offset + page.Items.Count) : string.Empty);

    private static string EncodePageToken(int offset) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static int DecodePageToken(string? pageToken)
    {
        if (string.IsNullOrWhiteSpace(pageToken))
        {
            return 0;
        }

        try
        {
            var value = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                && offset >= 0)
            {
                return offset;
            }
        }
        catch (FormatException)
        {
        }

        throw Invalid("pageToken", "Page token is invalid.");
    }

    private static Guid ParseId(string value, string field)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw Invalid(field, "A non-empty UUID is required.");
    }

    private static string NormalizeSlug(string value)
    {
        var slug = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SlugPattern().IsMatch(slug))
        {
            throw Invalid(
                "slug",
                "Slug must contain 3-64 lowercase letters, numbers, or hyphens and cannot start or end with a hyphen.");
        }

        return slug;
    }

    private static string NormalizeDisplayName(string value)
    {
        var displayName = value?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 100)
        {
            throw Invalid("displayName", "Display name must contain 1-100 characters.");
        }

        return displayName;
    }

    private static void ValidateEnvironmentType(PlatformEnvironmentType environmentType)
    {
        if (!Enum.IsDefined(environmentType))
        {
            throw Invalid("environmentType", "Environment type is invalid.");
        }
    }

    private static void RequireActive(PlatformResourceStatus status, string resource)
    {
        if (status != PlatformResourceStatus.Active)
        {
            throw FailedPrecondition(
                $"{resource}_archived",
                $"The {resource} is archived and must be restored first.");
        }
    }

    private static void RequireVersion(long currentVersion, long expectedVersion)
    {
        if (expectedVersion <= 0)
        {
            throw Invalid("expectedVersion", "Expected version must be positive.");
        }

        if (currentVersion != expectedVersion)
        {
            throw VersionConflict();
        }
    }

    private static AsterloomException Invalid(string field, string message) =>
        new(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = [message],
            });

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);

    private static AsterloomException AlreadyExists(string code, string message) =>
        new(AsterloomErrorKind.AlreadyExists, code, message);

    private static AsterloomException FailedPrecondition(string code, string message) =>
        new(AsterloomErrorKind.FailedPrecondition, code, message);

    private static AsterloomException VersionConflict() =>
        new(
            AsterloomErrorKind.Conflict,
            "version_conflict",
            "The resource changed since it was loaded. Reload and try again.");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}

public sealed record PlatformListResult<T>(IReadOnlyList<T> Items, string NextPageToken);
