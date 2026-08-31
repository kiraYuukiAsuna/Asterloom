using System.Security.Claims;
using Asterloom.Modules.Authorization.Model;
using Asterloom.Modules.Authorization.Persistence;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using AuthorizationArchivePolicyRuleRequest = Asterloom.Protocol.Authorization.Admin.V1.ArchivePolicyRuleRequest;
using AuthorizationRemoveRoleBindingRequest = Asterloom.Protocol.Authorization.Admin.V1.RemoveRoleBindingRequest;
using AuthorizationRestorePolicyRuleRequest = Asterloom.Protocol.Authorization.Admin.V1.RestorePolicyRuleRequest;

namespace Asterloom.Modules.Authorization;

internal sealed class AuthorizationInterceptor(
    AuthorizationDecisionService decisionService,
    IAuthorizationStore store,
    IServiceScopeFactory serviceScopeFactory) : Interceptor
{
    private const string AuthorizationRuntimeMethod =
        "/asterloom.authorization.v1.AuthorizationService/CheckPermission";

    private static readonly Dictionary<string, string> Permissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlatformMethod("GetPlatformInfo")] = "platform.info.read",
            [PlatformMethod("ListTenants")] = "platform.tenant.read",
            [PlatformMethod("CreateTenant")] = "platform.tenant.create",
            [PlatformMethod("UpdateTenant")] = "platform.tenant.update",
            [PlatformMethod("ArchiveTenant")] = "platform.tenant.archive",
            [PlatformMethod("RestoreTenant")] = "platform.tenant.restore",
            [PlatformMethod("ListApplications")] = "platform.application.read",
            [PlatformMethod("CreateApplication")] = "platform.application.create",
            [PlatformMethod("UpdateApplication")] = "platform.application.update",
            [PlatformMethod("ArchiveApplication")] = "platform.application.archive",
            [PlatformMethod("RestoreApplication")] = "platform.application.restore",
            [PlatformMethod("ListEnvironments")] = "platform.environment.read",
            [PlatformMethod("CreateEnvironment")] = "platform.environment.create",
            [PlatformMethod("UpdateEnvironment")] = "platform.environment.update",
            [PlatformMethod("ArchiveEnvironment")] = "platform.environment.archive",
            [PlatformMethod("RestoreEnvironment")] = "platform.environment.restore",
            [PlatformMethod("ListTenantMemberships")] =
                "platform.tenant.membership.read",
            [PlatformMethod("SetTenantMembership")] = "platform.tenant.membership.set",
            [PlatformMethod("RemoveTenantMembership")] =
                "platform.tenant.membership.remove",
            [AuthorizationMethod("ListPermissions")] = "authorization.permission.read",
            [AuthorizationMethod("ListRoles")] = "authorization.role.read",
            [AuthorizationMethod("CreateRole")] = "authorization.role.create",
            [AuthorizationMethod("UpdateRole")] = "authorization.role.update",
            [AuthorizationMethod("ArchiveRole")] = "authorization.role.archive",
            [AuthorizationMethod("RestoreRole")] = "authorization.role.restore",
            [AuthorizationMethod("ListRoleBindings")] = "authorization.binding.read",
            [AuthorizationMethod("SetRoleBinding")] = "authorization.binding.set",
            [AuthorizationMethod("RemoveRoleBinding")] = "authorization.binding.remove",
            [AuthorizationMethod("ListPolicyRules")] = "authorization.policy.read",
            [AuthorizationMethod("CreatePolicyRule")] = "authorization.policy.create",
            [AuthorizationMethod("UpdatePolicyRule")] = "authorization.policy.update",
            [AuthorizationMethod("ArchivePolicyRule")] = "authorization.policy.archive",
            [AuthorizationMethod("RestorePolicyRule")] = "authorization.policy.restore",
            [AuthorizationMethod("ListPolicyRevisions")] = "authorization.revision.read",
            [AuthorizationMethod("SimulateAuthorization")] =
                "authorization.simulation.execute",
            [AuditMethod("ListAuditEvents")] = "audit.event.read",
            [AuditMethod("GetAuditEvent")] = "audit.event.read",
            [AuditMethod("ExportAuditEvents")] = "audit.event.export",
            [IdentityMethod("ListUsers")] = "identity.user.read",
            [IdentityMethod("GetUser")] = "identity.user.read",
            [IdentityMethod("CreateUser")] = "identity.user.create",
            [IdentityMethod("InviteUser")] = "identity.user.invite",
            [IdentityMethod("ResendUserInvitation")] = "identity.user.invite",
            [IdentityMethod("UpdateUser")] = "identity.user.update",
            [IdentityMethod("SetUserRoles")] = "identity.user.roles.set",
            [IdentityMethod("ResetUserPassword")] = "identity.user.password.reset",
            [IdentityMethod("SuspendUser")] = "identity.user.suspend",
            [IdentityMethod("ReactivateUser")] = "identity.user.reactivate",
            [IdentityMethod("ArchiveUser")] = "identity.user.archive",
            [IdentityMethod("RestoreUser")] = "identity.user.restore",
            [IdentityMethod("ListUserSessions")] = "identity.session.read",
            [IdentityMethod("RevokeUserSession")] = "identity.session.revoke",
            [IdentityMethod("RevokeAllUserSessions")] = "identity.session.revoke",
            [IdentityMethod("ListApplicationMemberships")] =
                "identity.application-membership.read",
            [IdentityMethod("SetApplicationMembership")] =
                "identity.application-membership.set",
            [IdentityMethod("RemoveApplicationMembership")] =
                "identity.application-membership.remove",
            [IdentityMethod("ListClients")] = "identity.client.read",
            [IdentityMethod("GetClient")] = "identity.client.read",
            [IdentityMethod("CreateClient")] = "identity.client.create",
            [IdentityMethod("UpdateClient")] = "identity.client.update",
            [IdentityMethod("RotateClientSecret")] = "identity.client.secret.rotate",
            [IdentityMethod("DeleteClient")] = "identity.client.delete",
            [IdentityMethod("ListScopes")] = "identity.scope.read",
            [IdentityMethod("GetScope")] = "identity.scope.read",
            [IdentityMethod("CreateScope")] = "identity.scope.create",
            [IdentityMethod("UpdateScope")] = "identity.scope.update",
            [IdentityMethod("DeleteScope")] = "identity.scope.delete",
            [TargetingMethod("ListTargetingAttributes")] = "targeting.attribute.read",
            [TargetingMethod("ListSegments")] = "targeting.segment.read",
            [TargetingMethod("GetSegment")] = "targeting.segment.read",
            [TargetingMethod("CreateSegment")] = "targeting.segment.create",
            [TargetingMethod("UpdateSegment")] = "targeting.segment.update",
            [TargetingMethod("ArchiveSegment")] = "targeting.segment.archive",
            [TargetingMethod("RestoreSegment")] = "targeting.segment.restore",
            [TargetingMethod("SimulateTargeting")] = "targeting.simulation.execute",
            [FeatureAdminMethod("ListFlags")] = "feature.flag.read",
            [FeatureAdminMethod("GetFlag")] = "feature.flag.read",
            [FeatureAdminMethod("CreateFlag")] = "feature.flag.create",
            [FeatureAdminMethod("UpdateFlagDraft")] = "feature.flag.update",
            [FeatureAdminMethod("ValidateFlagDraft")] = "feature.flag.validate",
            [FeatureAdminMethod("PublishFlag")] = "feature.flag.publish",
            [FeatureAdminMethod("ListFlagRevisions")] = "feature.revision.read",
            [FeatureAdminMethod("RollbackFlag")] = "feature.flag.rollback",
            [FeatureAdminMethod("ArchiveFlag")] = "feature.flag.archive",
            [FeatureAdminMethod("RestoreFlag")] = "feature.flag.restore",
            [FeatureAdminMethod("SimulateFlag")] = "feature.simulation.execute",
            [FeatureRuntimeMethod("EvaluateFlag")] = "feature.flag.evaluate",
            [ConfigAdminMethod("ListConfigEntries")] = "config.entry.read",
            [ConfigAdminMethod("GetConfigEntry")] = "config.entry.read",
            [ConfigAdminMethod("CreateConfigEntry")] = "config.entry.create",
            [ConfigAdminMethod("UpdateConfigDraft")] = "config.entry.update",
            [ConfigAdminMethod("ValidateConfigDraft")] = "config.entry.validate",
            [ConfigAdminMethod("DiffConfigDraft")] = "config.diff.read",
            [ConfigAdminMethod("PublishConfigEntry")] = "config.entry.publish",
            [ConfigAdminMethod("ListConfigRevisions")] = "config.revision.read",
            [ConfigAdminMethod("RollbackConfigEntry")] = "config.entry.rollback",
            [ConfigAdminMethod("ArchiveConfigEntry")] = "config.entry.archive",
            [ConfigAdminMethod("RestoreConfigEntry")] = "config.entry.restore",
            [ConfigAdminMethod("PreviewConfigValue")] = "config.preview.execute",
            [ConfigAdminMethod("ListConfigSnapshots")] = "config.snapshot.history.read",
            [ConfigRuntimeMethod("GetConfigSnapshot")] = "config.snapshot.read",
            [ConfigRuntimeMethod("GetServerConfigSnapshot")] = "config.snapshot.server.read",
            [ConfigRuntimeMethod("CheckConfigUpdates")] = "config.update.check",
            [StorageMethod("ListBuckets")] = "storage.bucket.read",
            [StorageMethod("GetBucket")] = "storage.bucket.read",
            [StorageMethod("CreateBucket")] = "storage.bucket.create",
            [StorageMethod("UpdateBucket")] = "storage.bucket.update",
            [StorageMethod("ArchiveBucket")] = "storage.bucket.archive",
            [StorageMethod("RestoreBucket")] = "storage.bucket.restore",
            [StorageMethod("ListObjects")] = "storage.object.read",
            [StorageMethod("GetObject")] = "storage.object.read",
            [StorageMethod("UpdateObjectMetadata")] = "storage.object.metadata.update",
            [StorageMethod("CreateUploadSession")] = "storage.object.upload",
            [StorageMethod("CompleteUpload")] = "storage.object.upload",
            [StorageMethod("CreateDownloadUrl")] = "storage.object.download",
            [StorageMethod("CopyObject")] = "storage.object.copy",
            [StorageMethod("DeleteObject")] = "storage.object.delete",
            [ReleaseAdminMethod("ListSigningKeys")] = "release.signing-key.read",
            [ReleaseAdminMethod("CreateSigningKey")] = "release.signing-key.create",
            [ReleaseAdminMethod("ArchiveSigningKey")] = "release.signing-key.archive",
            [ReleaseAdminMethod("RestoreSigningKey")] = "release.signing-key.restore",
            [ReleaseAdminMethod("ListChannels")] = "release.channel.read",
            [ReleaseAdminMethod("GetChannel")] = "release.channel.read",
            [ReleaseAdminMethod("CreateChannel")] = "release.channel.create",
            [ReleaseAdminMethod("UpdateChannel")] = "release.channel.update",
            [ReleaseAdminMethod("ArchiveChannel")] = "release.channel.archive",
            [ReleaseAdminMethod("RestoreChannel")] = "release.channel.restore",
            [ReleaseAdminMethod("ListArtifacts")] = "release.artifact.read",
            [ReleaseAdminMethod("GetArtifact")] = "release.artifact.read",
            [ReleaseAdminMethod("CreateArtifactUpload")] = "release.artifact.upload",
            [ReleaseAdminMethod("CompleteArtifactUpload")] = "release.artifact.upload",
            [ReleaseAdminMethod("ArchiveArtifact")] = "release.artifact.archive",
            [ReleaseAdminMethod("ListReleases")] = "release.release.read",
            [ReleaseAdminMethod("GetRelease")] = "release.release.read",
            [ReleaseAdminMethod("CreateRelease")] = "release.release.create",
            [ReleaseAdminMethod("UpdateReleaseDraft")] = "release.release.update",
            [ReleaseAdminMethod("ValidateRelease")] = "release.release.validate",
            [ReleaseAdminMethod("PublishRelease")] = "release.release.publish",
            [ReleaseAdminMethod("PauseRelease")] = "release.release.pause",
            [ReleaseAdminMethod("PromoteRelease")] = "release.release.promote",
            [ReleaseAdminMethod("RollbackRelease")] = "release.release.rollback",
            [ReleaseAdminMethod("GetReleaseManifest")] = "release.manifest.read",
            [ReleaseAdminMethod("SimulateUpdate")] = "release.simulation.execute",
            [ReleaseRuntimeMethod("CheckForUpdate")] = "release.update.check",
            [AnalyticsAdminMethod("ListEventSchemas")] = "analytics.schema.read",
            [AnalyticsAdminMethod("GetEventSchema")] = "analytics.schema.read",
            [AnalyticsAdminMethod("CreateEventSchema")] = "analytics.schema.create",
            [AnalyticsAdminMethod("UpdateEventSchema")] = "analytics.schema.update",
            [AnalyticsAdminMethod("ArchiveEventSchema")] = "analytics.schema.archive",
            [AnalyticsAdminMethod("RestoreEventSchema")] = "analytics.schema.restore",
            [AnalyticsAdminMethod("ListWriteKeys")] = "analytics.write-key.read",
            [AnalyticsAdminMethod("CreateWriteKey")] = "analytics.write-key.create",
            [AnalyticsAdminMethod("RotateWriteKey")] = "analytics.write-key.rotate",
            [AnalyticsAdminMethod("RevokeWriteKey")] = "analytics.write-key.revoke",
            [AnalyticsAdminMethod("ListEvents")] = "analytics.event.read",
            [AnalyticsAdminMethod("GetEvent")] = "analytics.event.read",
            [AnalyticsAdminMethod("QueryAnalytics")] = "analytics.query.execute",
            [AnalyticsAdminMethod("UpdateRetention")] = "analytics.retention.update",
            [AnalyticsAdminMethod("ExportEvents")] = "analytics.event.export",
            [TelemetryAdminMethod("ListSources")] = "telemetry.source.read",
            [TelemetryAdminMethod("GetSource")] = "telemetry.source.read",
            [TelemetryAdminMethod("CreateSource")] = "telemetry.source.create",
            [TelemetryAdminMethod("UpdateSource")] = "telemetry.source.update",
            [TelemetryAdminMethod("ArchiveSource")] = "telemetry.source.archive",
            [TelemetryAdminMethod("RestoreSource")] = "telemetry.source.restore",
            [TelemetryAdminMethod("GetTelemetrySettings")] = "telemetry.settings.read",
            [TelemetryAdminMethod("UpdateTelemetrySettings")] = "telemetry.settings.update",
            [TelemetryAdminMethod("GetCollectorHealth")] = "telemetry.health.read",
            [TelemetryAdminMethod("ListRecentErrors")] = "telemetry.error.read",
            [TelemetryAdminMethod("GetDiagnosticLink")] = "telemetry.diagnostic.read",
            [OperationsAdminMethod("ListApis")] = "operations.api.read",
            [OperationsAdminMethod("GetOperationsHealth")] = "operations.health.read",
            [OperationsAdminMethod("GetOpenApiDocument")] = "operations.openapi.read",
        };

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (string.Equals(context.Method, AuthorizationRuntimeMethod, StringComparison.Ordinal))
        {
            return await continuation(request, context);
        }

        if (!Permissions.TryGetValue(context.Method, out var permission))
        {
            if (context.Method.Contains(".admin.", StringComparison.Ordinal))
            {
                throw Denied("unmapped_admin_permission");
            }

            return await continuation(request, context);
        }

        var principal = context.GetHttpContext().User;
        var actorId = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new AsterloomException(
                AsterloomErrorKind.Unauthenticated,
                "actor_identity_missing",
                "The access token has no stable subject.");
        var trustedRoles = principal.Claims
            .Where(claim => claim.Type is "role" || claim.Type == ClaimTypes.Role)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var scope = ApplicationTokenScope.Enforce(
            principal,
            await ResolveScopeAsync(request, context.CancellationToken),
            inferWhenUnspecified: false);
        await using (var membershipScope = serviceScopeFactory.CreateAsyncScope())
        {
            await ApplicationTokenScope.EnforceMembershipAsync(
                principal,
                membershipScope.ServiceProvider
                    .GetRequiredService<IApplicationMembershipValidator>(),
                context.CancellationToken);
        }
        var decision = await decisionService.DecideAsync(
            new AuthorizationDecisionRequest(actorId, scope, permission, trustedRoles),
            context.CancellationToken);
        if (!decision.Allowed)
        {
            throw Denied("permission_denied");
        }

        return await continuation(request, context);
    }

    private async Task<AuthorizationScope> ResolveScopeAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken)
    {
        if (request is AuthorizationRemoveRoleBindingRequest removeBinding)
        {
            var binding = await store.GetRoleBindingAsync(
                ParseRequiredId(removeBinding.BindingId, "bindingId"),
                cancellationToken);
            return binding?.Scope ?? AuthorizationScope.Global;
        }

        if (request is AuthorizationArchivePolicyRuleRequest archivePolicy)
        {
            return await ResolvePolicyScopeAsync(
                archivePolicy.PolicyRuleId,
                cancellationToken);
        }

        if (request is AuthorizationRestorePolicyRuleRequest restorePolicy)
        {
            return await ResolvePolicyScopeAsync(
                restorePolicy.PolicyRuleId,
                cancellationToken);
        }

        if (request is not IMessage message)
        {
            return AuthorizationScope.Global;
        }

        var scopeMessage = ReadMessage(message, "scope")
            ?? ReadMessage(ReadMessage(message, "input"), "scope");
        var source = scopeMessage ?? message;
        return new AuthorizationScope(
            ParseOptionalId(ReadString(source, "tenant_id"), "tenantId"),
            ParseOptionalId(ReadString(source, "application_id"), "applicationId"),
            ParseOptionalId(ReadString(source, "environment_id"), "environmentId"));
    }

    private async Task<AuthorizationScope> ResolvePolicyScopeAsync(
        string policyRuleId,
        CancellationToken cancellationToken)
    {
        var policyRule = await store.GetPolicyRuleAsync(
            ParseRequiredId(policyRuleId, "policyRuleId"),
            cancellationToken);
        return policyRule?.Scope ?? AuthorizationScope.Global;
    }

    private static IMessage? ReadMessage(IMessage? message, string fieldName)
    {
        var field = message?.Descriptor.FindFieldByName(fieldName);
        return field?.Accessor.GetValue(message!) as IMessage;
    }

    private static string ReadString(IMessage message, string fieldName)
    {
        var field = message.Descriptor.FindFieldByName(fieldName);
        return field?.Accessor.GetValue(message) as string ?? string.Empty;
    }

    private static Guid ParseRequiredId(string value, string field) =>
        ParseOptionalId(value, field)
        ?? throw Invalid(field);

    private static Guid? ParseOptionalId(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw Invalid(field);
    }

    private static AsterloomException Invalid(string field) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = ["A valid identifier is required."],
        });

    private static AsterloomException Denied(string errorCode) => new(
        AsterloomErrorKind.PermissionDenied,
        errorCode,
        "The caller does not have permission to perform this operation.");

    private static string PlatformMethod(string method) =>
        $"/asterloom.platform.admin.v1.PlatformAdminService/{method}";

    private static string AuthorizationMethod(string method) =>
        $"/asterloom.authorization.admin.v1.AuthorizationAdminService/{method}";

    private static string AuditMethod(string method) =>
        $"/asterloom.audit.admin.v1.AuditAdminService/{method}";

    private static string IdentityMethod(string method) =>
        $"/asterloom.identity.admin.v1.IdentityAdminService/{method}";

    private static string TargetingMethod(string method) =>
        $"/asterloom.targeting.admin.v1.TargetingAdminService/{method}";

    private static string FeatureAdminMethod(string method) =>
        $"/asterloom.feature.admin.v1.FeatureAdminService/{method}";

    private static string FeatureRuntimeMethod(string method) =>
        $"/asterloom.feature.v1.FeatureService/{method}";

    private static string ConfigAdminMethod(string method) =>
        $"/asterloom.config.admin.v1.ConfigAdminService/{method}";

    private static string ConfigRuntimeMethod(string method) =>
        $"/asterloom.config.v1.ConfigService/{method}";

    private static string StorageMethod(string method) =>
        $"/asterloom.storage.admin.v1.StorageAdminService/{method}";

    private static string ReleaseAdminMethod(string method) =>
        $"/asterloom.release.admin.v1.ReleaseAdminService/{method}";

    private static string ReleaseRuntimeMethod(string method) =>
        $"/asterloom.release.v1.ReleaseService/{method}";

    private static string AnalyticsAdminMethod(string method) =>
        $"/asterloom.analytics.admin.v1.AnalyticsAdminService/{method}";

    private static string TelemetryAdminMethod(string method) =>
        $"/asterloom.telemetry.admin.v1.TelemetryAdminService/{method}";

    private static string OperationsAdminMethod(string method) =>
        $"/asterloom.operations.admin.v1.OperationsAdminService/{method}";
}
