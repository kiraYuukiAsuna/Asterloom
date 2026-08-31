using System.Text.Json;
using System.Collections.Immutable;
using OpenIddict.Abstractions;

namespace Asterloom.Modules.Identity;

internal sealed record IdentityClientApplicationBinding(
    Guid TenantId,
    Guid ApplicationId,
    bool AllowUserRegistration,
    bool AllowMembershipAutoJoin);

internal static class IdentityClientApplicationMetadata
{
    private const string TenantIdProperty = "asterloom:tenant_id";
    private const string ApplicationIdProperty = "asterloom:application_id";
    private const string AllowUserRegistrationProperty =
        "asterloom:allow_user_registration";
    private const string AllowMembershipAutoJoinProperty =
        "asterloom:allow_membership_auto_join";

    public static void Apply(
        OpenIddictApplicationDescriptor descriptor,
        IdentityClientApplicationBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Properties.Remove(TenantIdProperty);
        descriptor.Properties.Remove(ApplicationIdProperty);
        descriptor.Properties.Remove(AllowUserRegistrationProperty);
        descriptor.Properties.Remove(AllowMembershipAutoJoinProperty);
        if (binding is null)
        {
            return;
        }

        descriptor.Properties[TenantIdProperty] =
            JsonSerializer.SerializeToElement(binding.TenantId.ToString("D"));
        descriptor.Properties[ApplicationIdProperty] =
            JsonSerializer.SerializeToElement(binding.ApplicationId.ToString("D"));
        descriptor.Properties[AllowUserRegistrationProperty] =
            JsonSerializer.SerializeToElement(binding.AllowUserRegistration);
        descriptor.Properties[AllowMembershipAutoJoinProperty] =
            JsonSerializer.SerializeToElement(binding.AllowMembershipAutoJoin);
    }

    public static async Task<IdentityClientApplicationBinding?> ReadAsync(
        IOpenIddictApplicationManager applicationManager,
        object application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationManager);
        ArgumentNullException.ThrowIfNull(application);
        var properties = await applicationManager.GetPropertiesAsync(
            application,
            cancellationToken);
        var hasTenant = properties.ContainsKey(TenantIdProperty);
        var hasApplication = properties.ContainsKey(ApplicationIdProperty);
        if (!hasTenant && !hasApplication)
        {
            return null;
        }

        if (!TryReadGuid(properties, TenantIdProperty, out var tenantId)
            || !TryReadGuid(properties, ApplicationIdProperty, out var applicationId))
        {
            throw new InvalidOperationException(
                "The OIDC client contains an invalid Platform application binding.");
        }

        return new IdentityClientApplicationBinding(
            tenantId,
            applicationId,
            ReadBoolean(properties, AllowUserRegistrationProperty),
            ReadBoolean(properties, AllowMembershipAutoJoinProperty));
    }

    public static async Task<IdentityClientApplicationBinding?> FindAsync(
        IOpenIddictApplicationManager applicationManager,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(
            clientId,
            cancellationToken);
        return application is null
            ? null
            : await ReadAsync(applicationManager, application, cancellationToken);
    }

    private static bool TryReadGuid(
        ImmutableDictionary<string, JsonElement> properties,
        string key,
        out Guid value)
    {
        value = Guid.Empty;
        return properties.TryGetValue(key, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value)
            && value != Guid.Empty;
    }

    private static bool ReadBoolean(
        ImmutableDictionary<string, JsonElement> properties,
        string key)
    {
        if (!properties.TryGetValue(key, out var property))
        {
            return false;
        }

        return property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new InvalidOperationException(
                $"The OIDC client contains an invalid '{key}' property.");
    }
}
