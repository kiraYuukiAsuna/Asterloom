using System.Security.Cryptography;
using System.Text;
using Asterloom.Modules.Authorization.Model;

namespace Asterloom.Modules.Authorization;

public static class AuthorizationCatalog
{
    private static readonly PermissionDefinition[] PermissionItems =
    [
        Permission("platform.info.read", "Read platform information"),
        Permission("platform.tenant.read", "Read tenants"),
        Permission("platform.tenant.create", "Create tenants"),
        Permission("platform.tenant.update", "Update tenants"),
        Permission("platform.tenant.archive", "Archive tenants"),
        Permission("platform.tenant.restore", "Restore tenants"),
        Permission("platform.application.read", "Read applications"),
        Permission("platform.application.create", "Create applications"),
        Permission("platform.application.update", "Update applications"),
        Permission("platform.application.archive", "Archive applications"),
        Permission("platform.application.restore", "Restore applications"),
        Permission("platform.environment.read", "Read environments"),
        Permission("platform.environment.create", "Create environments"),
        Permission("platform.environment.update", "Update environments"),
        Permission("platform.environment.archive", "Archive environments"),
        Permission("platform.environment.restore", "Restore environments"),
        Permission("platform.tenant.membership.read", "Read tenant memberships"),
        Permission("platform.tenant.membership.set", "Set tenant memberships"),
        Permission("platform.tenant.membership.remove", "Remove tenant memberships"),
        Permission("authorization.permission.read", "Read permission catalog"),
        Permission("authorization.permission.create", "Create application permissions"),
        Permission("authorization.permission.update", "Update application permissions"),
        Permission("authorization.permission.archive", "Archive application permissions"),
        Permission("authorization.permission.restore", "Restore application permissions"),
        Permission("authorization.role.read", "Read authorization roles"),
        Permission("authorization.role.create", "Create authorization roles"),
        Permission("authorization.role.update", "Update authorization roles"),
        Permission("authorization.role.archive", "Archive authorization roles"),
        Permission("authorization.role.restore", "Restore authorization roles"),
        Permission("authorization.binding.read", "Read role bindings"),
        Permission("authorization.binding.set", "Set role bindings"),
        Permission("authorization.binding.remove", "Remove role bindings"),
        Permission("authorization.policy.read", "Read policy rules"),
        Permission("authorization.policy.create", "Create policy rules"),
        Permission("authorization.policy.update", "Update policy rules"),
        Permission("authorization.policy.archive", "Archive policy rules"),
        Permission("authorization.policy.restore", "Restore policy rules"),
        Permission("authorization.revision.read", "Read policy revisions"),
        Permission("authorization.simulation.execute", "Simulate authorization decisions"),
        Permission("audit.event.read", "Read audit events"),
        Permission("audit.event.export", "Export audit events"),
        Permission("identity.user.read", "Read Passport users"),
        Permission("identity.user.create", "Create Passport users"),
        Permission("identity.user.invite", "Invite Passport users"),
        Permission("identity.user.update", "Update Passport users"),
        Permission("identity.user.roles.set", "Set Passport user roles"),
        Permission("identity.user.suspend", "Suspend Passport users"),
        Permission("identity.user.reactivate", "Reactivate Passport users"),
        Permission("identity.user.archive", "Archive Passport users"),
        Permission("identity.user.restore", "Restore Passport users"),
        Permission("identity.user.password.reset", "Reset Passport user passwords"),
        Permission("identity.session.read", "Read Passport user sessions"),
        Permission("identity.session.revoke", "Revoke Passport user sessions"),
        Permission("identity.application-membership.read", "Read application memberships"),
        Permission("identity.application-membership.set", "Set application memberships"),
        Permission("identity.application-membership.remove", "Remove application memberships"),
        Permission("identity.client.read", "Read OIDC clients"),
        Permission("identity.client.create", "Create OIDC clients"),
        Permission("identity.client.update", "Update OIDC clients"),
        Permission("identity.client.secret.rotate", "Rotate OIDC client secrets"),
        Permission("identity.client.delete", "Delete OIDC clients"),
        Permission("identity.scope.read", "Read OIDC scopes"),
        Permission("identity.scope.create", "Create OIDC scopes"),
        Permission("identity.scope.update", "Update OIDC scopes"),
        Permission("identity.scope.delete", "Delete OIDC scopes"),
        Permission("mail.account.read", "Read SMTP accounts"),
        Permission("mail.account.create", "Create SMTP accounts"),
        Permission("mail.account.update", "Update SMTP accounts"),
        Permission("mail.account.archive", "Archive SMTP accounts"),
        Permission("mail.account.restore", "Restore SMTP accounts"),
        Permission("mail.account.test", "Test SMTP accounts"),
        Permission("mail.delivery.read", "Read mail delivery history"),
        Permission("mail.delivery.send", "Send application email"),
        Permission("targeting.attribute.read", "Read targeting attribute catalog"),
        Permission("targeting.segment.read", "Read targeting segments"),
        Permission("targeting.segment.create", "Create targeting segments"),
        Permission("targeting.segment.update", "Update targeting segments"),
        Permission("targeting.segment.archive", "Archive targeting segments"),
        Permission("targeting.segment.restore", "Restore targeting segments"),
        Permission("targeting.simulation.execute", "Simulate targeting evaluations"),
        Permission("feature.flag.read", "Read feature flags"),
        Permission("feature.flag.create", "Create feature flags"),
        Permission("feature.flag.update", "Update feature flag drafts"),
        Permission("feature.flag.validate", "Validate feature flag drafts"),
        Permission("feature.flag.publish", "Publish feature flags"),
        Permission("feature.flag.rollback", "Roll back feature flags"),
        Permission("feature.flag.archive", "Archive feature flags"),
        Permission("feature.flag.restore", "Restore feature flags"),
        Permission("feature.flag.evaluate", "Evaluate published feature flags"),
        Permission("feature.revision.read", "Read feature flag revisions"),
        Permission("feature.simulation.execute", "Simulate feature flag evaluations"),
        Permission("config.entry.read", "Read configuration entries"),
        Permission("config.entry.create", "Create configuration entries"),
        Permission("config.entry.update", "Update configuration drafts"),
        Permission("config.entry.validate", "Validate configuration drafts"),
        Permission("config.entry.publish", "Publish configuration entries"),
        Permission("config.entry.rollback", "Roll back configuration entries"),
        Permission("config.entry.archive", "Archive configuration entries"),
        Permission("config.entry.restore", "Restore configuration entries"),
        Permission("config.diff.read", "Read configuration draft diffs"),
        Permission("config.revision.read", "Read configuration revisions"),
        Permission("config.preview.execute", "Preview effective configuration values"),
        Permission("config.snapshot.read", "Read client configuration snapshots"),
        Permission("config.snapshot.server.read", "Read server configuration snapshots"),
        Permission("config.snapshot.history.read", "Read configuration snapshot history"),
        Permission("config.update.check", "Check configuration updates"),
        Permission("storage.bucket.read", "Read storage buckets"),
        Permission("storage.bucket.create", "Create storage buckets"),
        Permission("storage.bucket.update", "Update storage buckets"),
        Permission("storage.bucket.archive", "Archive storage buckets"),
        Permission("storage.bucket.restore", "Restore storage buckets"),
        Permission("storage.object.read", "Read object metadata"),
        Permission("storage.object.metadata.update", "Update object metadata"),
        Permission("storage.object.upload", "Upload storage objects"),
        Permission("storage.object.download", "Download storage objects"),
        Permission("storage.object.copy", "Copy storage objects"),
        Permission("storage.object.delete", "Delete storage objects"),
        Permission("release.signing-key.read", "Read release signing keys"),
        Permission("release.signing-key.create", "Create release signing keys"),
        Permission("release.signing-key.archive", "Archive release signing keys"),
        Permission("release.signing-key.restore", "Restore release signing keys"),
        Permission("release.channel.read", "Read release channels"),
        Permission("release.channel.create", "Create release channels"),
        Permission("release.channel.update", "Update release channels"),
        Permission("release.channel.archive", "Archive release channels"),
        Permission("release.channel.restore", "Restore release channels"),
        Permission("release.artifact.read", "Read release artifacts"),
        Permission("release.artifact.upload", "Upload release artifacts"),
        Permission("release.artifact.archive", "Archive release artifacts"),
        Permission("release.release.read", "Read desktop releases"),
        Permission("release.release.create", "Create desktop releases"),
        Permission("release.release.update", "Update desktop release drafts"),
        Permission("release.release.validate", "Validate desktop releases"),
        Permission("release.release.publish", "Publish desktop releases"),
        Permission("release.release.pause", "Pause desktop releases"),
        Permission("release.release.promote", "Promote desktop releases"),
        Permission("release.release.rollback", "Roll back desktop releases"),
        Permission("release.manifest.read", "Read signed release manifests"),
        Permission("release.simulation.execute", "Simulate desktop update decisions"),
        Permission("release.update.check", "Check for desktop updates"),
        Permission("analytics.schema.read", "Read analytics event schemas"),
        Permission("analytics.schema.create", "Create analytics event schemas"),
        Permission("analytics.schema.update", "Update analytics event schemas"),
        Permission("analytics.schema.archive", "Archive analytics event schemas"),
        Permission("analytics.schema.restore", "Restore analytics event schemas"),
        Permission("analytics.retention.update", "Update analytics retention policies"),
        Permission("analytics.write-key.read", "Read analytics write keys"),
        Permission("analytics.write-key.create", "Create analytics write keys"),
        Permission("analytics.write-key.rotate", "Rotate analytics write keys"),
        Permission("analytics.write-key.revoke", "Revoke analytics write keys"),
        Permission("analytics.event.read", "Read analytics events"),
        Permission("analytics.query.execute", "Run analytics aggregation queries"),
        Permission("analytics.event.export", "Export analytics events"),
        Permission("telemetry.source.read", "Read telemetry sources"),
        Permission("telemetry.source.create", "Create telemetry sources"),
        Permission("telemetry.source.update", "Update telemetry sources"),
        Permission("telemetry.source.archive", "Archive telemetry sources"),
        Permission("telemetry.source.restore", "Restore telemetry sources"),
        Permission("telemetry.settings.read", "Read telemetry settings"),
        Permission("telemetry.settings.update", "Update telemetry settings"),
        Permission("telemetry.health.read", "Read Collector health"),
        Permission("telemetry.error.read", "Read recent telemetry errors"),
        Permission("telemetry.record.read", "Read stored telemetry records"),
        Permission("telemetry.diagnostic.read", "Open telemetry diagnostics"),
        Permission("operations.api.read", "Read the API catalog"),
        Permission("operations.health.read", "Read platform dependency health"),
        Permission("operations.openapi.read", "Read the OpenAPI document"),
    ];

    private static readonly AuthorizationRole[] SystemRoleItems = CreateSystemRoles();
    private static readonly Dictionary<string, AuthorizationRole> SystemRolesByKey =
        SystemRoleItems.ToDictionary(static role => role.Key, StringComparer.Ordinal);
    private static readonly Dictionary<Guid, AuthorizationRole> SystemRolesById =
        SystemRoleItems.ToDictionary(static role => role.Id);

    public static IReadOnlyList<PermissionDefinition> Permissions => PermissionItems;

    public static IReadOnlyList<AuthorizationRole> SystemRoles => SystemRoleItems;

    public static bool IsKnownPermission(string permission) =>
        permission == "*"
        || PermissionItems.Any(item => string.Equals(
            item.Key,
            permission,
            StringComparison.Ordinal));

    public static bool IsReservedApplicationPermission(string permission)
    {
        var separator = permission.IndexOfAny(['.', '_', '-']);
        var prefix = separator < 0 ? permission : permission[..separator];
        return string.Equals(prefix, "asterloom", StringComparison.Ordinal)
            || PermissionItems.Any(item => string.Equals(
                item.Module,
                prefix,
                StringComparison.Ordinal));
    }

    public static AuthorizationRole? FindSystemRole(Guid id) =>
        SystemRolesById.GetValueOrDefault(id);

    public static AuthorizationRole? FindSystemRole(string key) =>
        SystemRolesByKey.GetValueOrDefault(key);

    public static string? MapTrustedRole(string claim) => claim switch
    {
        "SuperAdministrator" => "super-administrator",
        "TenantAdministrator" => "tenant-administrator",
        "Operator" => "operator",
        "Developer" => "developer",
        "Viewer" => "viewer",
        _ => null,
    };

    private static PermissionDefinition Permission(string key, string displayName) =>
        new(
            StablePermissionId(key),
            key,
            displayName,
            $"Allows an actor to {displayName.ToLowerInvariant()}.",
            key.Split('.', 2)[0],
            AuthorizationScope.Global,
            IsSystem: true,
            AuthorizationResourceStatus.Active,
            Version: 1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            ArchivedAt: null);

    private static Guid StablePermissionId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"asterloom:permission:{key}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static AuthorizationRole[] CreateSystemRoles()
    {
        var timestamp = DateTimeOffset.UnixEpoch;
        var readPermissions = PermissionItems
            .Where(static permission =>
                permission.Key.EndsWith(".read", StringComparison.Ordinal)
                && !permission.Key.StartsWith("audit.", StringComparison.Ordinal)
                && !permission.Key.StartsWith("identity.", StringComparison.Ordinal)
                && permission.Key is not "config.snapshot.server.read")
            .Select(static permission => permission.Key)
            .ToArray();
        var tenantAdministratorPermissions = PermissionItems
            .Where(static permission =>
                permission.Key.StartsWith("platform.application.", StringComparison.Ordinal)
                || permission.Key.StartsWith("platform.environment.", StringComparison.Ordinal)
                || permission.Key.StartsWith("platform.tenant.membership.", StringComparison.Ordinal)
                || permission.Key.StartsWith("authorization.", StringComparison.Ordinal)
                || permission.Key.StartsWith("targeting.", StringComparison.Ordinal)
                || permission.Key.StartsWith("feature.", StringComparison.Ordinal)
                || permission.Key.StartsWith("config.", StringComparison.Ordinal)
                || permission.Key.StartsWith("storage.", StringComparison.Ordinal)
                || permission.Key.StartsWith("release.", StringComparison.Ordinal)
                || permission.Key.StartsWith("analytics.", StringComparison.Ordinal)
                || permission.Key.StartsWith("telemetry.", StringComparison.Ordinal)
                || permission.Key.StartsWith("operations.", StringComparison.Ordinal)
                || permission.Key.StartsWith("mail.", StringComparison.Ordinal)
                || permission.Key is "platform.info.read"
                    or "platform.tenant.read"
                    or "platform.tenant.update"
                    or "authorization.permission.read")
            .Select(static permission => permission.Key)
            .ToArray();
        var operatorPermissions = PermissionItems
            .Where(static permission =>
                permission.Key is not "config.snapshot.server.read"
                && ((permission.Key.EndsWith(".read", StringComparison.Ordinal)
                        && !permission.Key.StartsWith("audit.", StringComparison.Ordinal)
                        && !permission.Key.StartsWith("identity.", StringComparison.Ordinal))
                    || permission.Key is "platform.application.update"
                        or "platform.environment.update"
                        or "authorization.simulation.execute"
                        or "targeting.simulation.execute"
                        or "feature.flag.publish"
                        or "feature.flag.rollback"
                        or "feature.flag.evaluate"
                        or "feature.simulation.execute"
                        or "config.entry.publish"
                        or "config.entry.rollback"
                        or "config.preview.execute"
                        or "config.update.check"
                        or "storage.object.upload"
                        or "storage.object.download"
                        or "storage.object.copy"
                        or "storage.object.delete"
                        or "release.artifact.upload"
                            or "release.release.publish"
                            or "release.release.pause"
                            or "release.release.promote"
                            or "release.release.rollback"
                            or "release.simulation.execute"
                            or "release.update.check"
                            or "analytics.query.execute"
                            or "analytics.event.export"
                            or "telemetry.settings.update"
                            or "telemetry.source.update"
                            or "telemetry.source.archive"
                            or "telemetry.source.restore"
                    || permission.Key is "mail.account.test"
                        or "mail.delivery.send"))
            .Select(static permission => permission.Key)
            .ToArray();
        var developerPermissions = PermissionItems
            .Where(static permission =>
                permission.Key is not "config.snapshot.server.read"
                && ((permission.Key.EndsWith(".read", StringComparison.Ordinal)
                        && !permission.Key.StartsWith("audit.", StringComparison.Ordinal)
                        && !permission.Key.StartsWith("identity.", StringComparison.Ordinal))
                    || permission.Key is "platform.environment.create"
                        or "platform.environment.update"
                        or "authorization.simulation.execute"
                        or "targeting.segment.create"
                        or "targeting.segment.update"
                        or "targeting.segment.archive"
                        or "targeting.segment.restore"
                        or "targeting.simulation.execute"
                        or "feature.flag.create"
                        or "feature.flag.update"
                        or "feature.flag.validate"
                        or "feature.flag.evaluate"
                        or "feature.simulation.execute"
                        or "config.entry.create"
                        or "config.entry.update"
                        or "config.entry.validate"
                        or "config.preview.execute"
                        or "config.update.check"
                        || permission.Key.StartsWith("storage.", StringComparison.Ordinal)
                        || permission.Key.StartsWith("release.artifact.", StringComparison.Ordinal)
                        || permission.Key.StartsWith("release.release.", StringComparison.Ordinal)
                        || permission.Key is "release.simulation.execute"
                            or "release.update.check"
                        || permission.Key.StartsWith("analytics.", StringComparison.Ordinal)
                        || permission.Key.StartsWith("telemetry.", StringComparison.Ordinal)
                    || permission.Key.StartsWith("mail.", StringComparison.Ordinal)))
            .Select(static permission => permission.Key)
            .ToArray();

        return
        [
            SystemRole(
                "11111111-1111-7111-8111-111111111111",
                "super-administrator",
                "Super Administrator",
                "Unrestricted platform administrator.",
                ["*"],
                timestamp),
            SystemRole(
                "22222222-2222-7222-8222-222222222222",
                "tenant-administrator",
                "Tenant Administrator",
                "Administers resources and authorization inside an assigned tenant.",
                tenantAdministratorPermissions,
                timestamp),
            SystemRole(
                "33333333-3333-7333-8333-333333333333",
                "operator",
                "Operator",
                "Operates existing resources without changing access policy.",
                operatorPermissions,
                timestamp),
            SystemRole(
                "44444444-4444-7444-8444-444444444444",
                "developer",
                "Developer",
                "Develops and tests capabilities in assigned scopes.",
                developerPermissions,
                timestamp),
            SystemRole(
                "55555555-5555-7555-8555-555555555555",
                "viewer",
                "Viewer",
                "Read-only access to assigned scopes.",
                readPermissions,
                timestamp),
        ];
    }

    private static AuthorizationRole SystemRole(
        string id,
        string key,
        string displayName,
        string description,
        IReadOnlyList<string> permissions,
        DateTimeOffset timestamp) =>
        new(
            Guid.Parse(id),
            key,
            displayName,
            description,
            permissions,
            IsSystem: true,
            AuthorizationScope.Global,
            AuthorizationResourceStatus.Active,
            Version: 1,
            timestamp,
            timestamp,
            ArchivedAt: null);
}
