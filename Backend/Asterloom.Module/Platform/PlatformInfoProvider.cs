using System.Reflection;
using Asterloom.Protocol.Platform.Admin.V1;
using Google.Protobuf.WellKnownTypes;

namespace Asterloom.Modules.Platform;

public sealed class PlatformInfoProvider(TimeProvider timeProvider)
{
    private static readonly (string Key, string Name, CapabilityLifecycle Lifecycle)[] Capabilities =
    [
        ("identity", "Identity", CapabilityLifecycle.Available),
        ("authorization", "Authorization", CapabilityLifecycle.Available),
        ("feature", "Feature flags", CapabilityLifecycle.Available),
        ("targeting", "Targeting and rollout", CapabilityLifecycle.Available),
        ("config", "Dynamic configuration", CapabilityLifecycle.Available),
        ("release", "Desktop updates", CapabilityLifecycle.Available),
        ("analytics", "Analytics", CapabilityLifecycle.Available),
        ("telemetry", "Telemetry", CapabilityLifecycle.Available),
        ("mail", "Application email", CapabilityLifecycle.Available),
        ("rpc", "RPC and HTTP", CapabilityLifecycle.Available),
        ("storage", "File storage", CapabilityLifecycle.Available),
        ("persistence", "Persistence", CapabilityLifecycle.Available),
        ("web", "Web management console", CapabilityLifecycle.Available),
    ];

    public GetPlatformInfoResponse GetPlatformInfo()
    {
        var assembly = typeof(PlatformInfoProvider).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var response = new GetPlatformInfoResponse
        {
            Name = "Asterloom",
            Version = NormalizeVersion(informationalVersion, assembly.GetName().Version),
            Status = PlatformStatus.Operational,
            ServerTime = Timestamp.FromDateTimeOffset(timeProvider.GetUtcNow()),
        };

        response.Capabilities.AddRange(
            Capabilities.Select(capability => new PlatformCapability
            {
                Key = capability.Key,
                DisplayName = capability.Name,
                Lifecycle = capability.Lifecycle,
            }));

        return response;
    }

    private static string NormalizeVersion(string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }
}
