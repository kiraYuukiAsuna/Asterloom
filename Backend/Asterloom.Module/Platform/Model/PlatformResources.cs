namespace Asterloom.Modules.Platform.Model;

public enum PlatformResourceStatus : short
{
    Active = 1,
    Archived = 2,
}

public enum PlatformEnvironmentType : short
{
    Development = 1,
    Staging = 2,
    Production = 3,
}

public enum PlatformMembershipStatus : short
{
    Active = 1,
    Removed = 2,
}

public sealed record PlatformTenant(
    Guid Id,
    string Slug,
    string DisplayName,
    PlatformResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record PlatformApplication(
    Guid Id,
    Guid TenantId,
    string Slug,
    string DisplayName,
    PlatformResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record PlatformEnvironment(
    Guid Id,
    Guid TenantId,
    Guid ApplicationId,
    string Slug,
    string DisplayName,
    PlatformEnvironmentType EnvironmentType,
    bool IsProtected,
    PlatformResourceStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record PlatformTenantMembership(
    Guid TenantId,
    Guid ActorId,
    PlatformMembershipStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PlatformPage<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record PlatformPageRequest(
    int Offset,
    int Limit,
    string? Query,
    bool IncludeInactive);
