using Asterloom.Modules.Platform.Model;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Protobuf.WellKnownTypes;
using ProtocolApplication = Asterloom.Protocol.Platform.Admin.V1.Application;
using ProtocolEnvironment = Asterloom.Protocol.Platform.Admin.V1.Environment;

namespace Asterloom.Modules.Platform;

internal static class PlatformProtocolMapper
{
    public static Tenant ToProtocol(this PlatformTenant tenant) => new()
    {
        Id = tenant.Id.ToString("D"),
        Slug = tenant.Slug,
        DisplayName = tenant.DisplayName,
        Status = tenant.Status.ToProtocol(),
        Version = tenant.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(tenant.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(tenant.UpdatedAt),
        ArchivedAt = tenant.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolApplication ToProtocol(this PlatformApplication application) => new()
    {
        Id = application.Id.ToString("D"),
        TenantId = application.TenantId.ToString("D"),
        Slug = application.Slug,
        DisplayName = application.DisplayName,
        Status = application.Status.ToProtocol(),
        Version = application.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(application.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(application.UpdatedAt),
        ArchivedAt = application.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolEnvironment ToProtocol(this PlatformEnvironment environment) => new()
    {
        Id = environment.Id.ToString("D"),
        TenantId = environment.TenantId.ToString("D"),
        ApplicationId = environment.ApplicationId.ToString("D"),
        Slug = environment.Slug,
        DisplayName = environment.DisplayName,
        EnvironmentType = environment.EnvironmentType.ToProtocol(),
        IsProtected = environment.IsProtected,
        Status = environment.Status.ToProtocol(),
        Version = environment.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(environment.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(environment.UpdatedAt),
        ArchivedAt = environment.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static TenantMembership ToProtocol(this PlatformTenantMembership membership) => new()
    {
        TenantId = membership.TenantId.ToString("D"),
        ActorId = membership.ActorId.ToString("D"),
        Status = membership.Status switch
        {
            PlatformMembershipStatus.Active => MembershipStatus.Active,
            PlatformMembershipStatus.Removed => MembershipStatus.Removed,
            _ => MembershipStatus.Unspecified,
        },
        Version = membership.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(membership.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(membership.UpdatedAt),
    };

    private static ResourceStatus ToProtocol(this PlatformResourceStatus status) => status switch
    {
        PlatformResourceStatus.Active => ResourceStatus.Active,
        PlatformResourceStatus.Archived => ResourceStatus.Archived,
        _ => ResourceStatus.Unspecified,
    };

    private static EnvironmentType ToProtocol(this PlatformEnvironmentType environmentType) =>
        environmentType switch
        {
            PlatformEnvironmentType.Development => EnvironmentType.Development,
            PlatformEnvironmentType.Staging => EnvironmentType.Staging,
            PlatformEnvironmentType.Production => EnvironmentType.Production,
            _ => EnvironmentType.Unspecified,
        };
}
