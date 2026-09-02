using System.Collections.Immutable;
using System.Text.Json;
using OpenIddict.Abstractions;

namespace Asterloom.Modules.Identity;

internal static class IdentitySystemResourceMetadata
{
    private const string ConfigurationManagedProperty =
        "asterloom:configuration_managed";

    public static void MarkConfigurationManaged(
        OpenIddictApplicationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Properties[ConfigurationManagedProperty] =
            JsonSerializer.SerializeToElement(true);
    }

    public static bool IsConfigurationManaged(
        OpenIddictApplicationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return IsConfigurationManaged(descriptor.Properties.ToImmutableDictionary());
    }

    public static async Task<bool> IsConfigurationManagedAsync(
        IOpenIddictApplicationManager applicationManager,
        object application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationManager);
        ArgumentNullException.ThrowIfNull(application);
        return IsConfigurationManaged(
            await applicationManager.GetPropertiesAsync(application, cancellationToken));
    }

    private static bool IsConfigurationManaged(
        ImmutableDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(ConfigurationManagedProperty, out var property)
        && property.ValueKind is JsonValueKind.True;
}
