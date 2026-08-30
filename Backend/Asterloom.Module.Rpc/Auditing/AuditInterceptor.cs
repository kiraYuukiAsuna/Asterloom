using System.Security.Claims;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Errors;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Rpc.Auditing;

internal sealed class AuditInterceptor(
    IAuditStore store,
    TimeProvider timeProvider,
    ILogger<AuditInterceptor> logger) : Interceptor
{
    private static readonly string[] AuditableMethodPrefixes =
    [
        "Create",
        "Update",
        "Archive",
        "Restore",
        "Set",
        "Remove",
        "Delete",
        "Import",
        "Publish",
        "Rollback",
        "Rotate",
        "Revoke",
        "Export",
        "Invite",
        "Resend",
        "Suspend",
        "Reactivate",
        "Complete",
        "Copy",
        "Pause",
        "Promote",
    ];

    private static readonly Dictionary<string, AuditResourceDescriptor> Resources =
        new(StringComparer.Ordinal)
        {
            [PlatformMethod("CreateTenant")] = new("tenant", null),
            [PlatformMethod("UpdateTenant")] = new("tenant", "tenant_id"),
            [PlatformMethod("ArchiveTenant")] = new("tenant", "tenant_id"),
            [PlatformMethod("RestoreTenant")] = new("tenant", "tenant_id"),
            [PlatformMethod("CreateApplication")] = new("application", null),
            [PlatformMethod("UpdateApplication")] = new("application", "application_id"),
            [PlatformMethod("ArchiveApplication")] = new("application", "application_id"),
            [PlatformMethod("RestoreApplication")] = new("application", "application_id"),
            [PlatformMethod("CreateEnvironment")] = new("environment", null),
            [PlatformMethod("UpdateEnvironment")] = new("environment", "environment_id"),
            [PlatformMethod("ArchiveEnvironment")] = new("environment", "environment_id"),
            [PlatformMethod("RestoreEnvironment")] = new("environment", "environment_id"),
            [PlatformMethod("SetTenantMembership")] = new("tenant_membership", "actor_id"),
            [PlatformMethod("RemoveTenantMembership")] = new("tenant_membership", "actor_id"),
            [AuthorizationMethod("CreateRole")] = new("authorization_role", null),
            [AuthorizationMethod("UpdateRole")] = new("authorization_role", "role_id"),
            [AuthorizationMethod("ArchiveRole")] = new("authorization_role", "role_id"),
            [AuthorizationMethod("RestoreRole")] = new("authorization_role", "role_id"),
            [AuthorizationMethod("SetRoleBinding")] = new("authorization_role_binding", "binding_id"),
            [AuthorizationMethod("RemoveRoleBinding")] = new("authorization_role_binding", "binding_id"),
            [AuthorizationMethod("CreatePolicyRule")] = new("authorization_policy_rule", null),
            [AuthorizationMethod("UpdatePolicyRule")] = new("authorization_policy_rule", "policy_rule_id"),
            [AuthorizationMethod("ArchivePolicyRule")] = new("authorization_policy_rule", "policy_rule_id"),
            [AuthorizationMethod("RestorePolicyRule")] = new("authorization_policy_rule", "policy_rule_id"),
            [AuditMethod("ExportAuditEvents")] = new("audit_export", null),
            [IdentityMethod("InviteUser")] = new("identity_user", null),
            [IdentityMethod("ResendUserInvitation")] = new("identity_user", "user_id"),
            [IdentityMethod("UpdateUser")] = new("identity_user", "user_id"),
            [IdentityMethod("SetUserRoles")] = new("identity_user", "user_id"),
            [IdentityMethod("SuspendUser")] = new("identity_user", "user_id"),
            [IdentityMethod("ReactivateUser")] = new("identity_user", "user_id"),
            [IdentityMethod("ArchiveUser")] = new("identity_user", "user_id"),
            [IdentityMethod("RestoreUser")] = new("identity_user", "user_id"),
            [IdentityMethod("RevokeUserSession")] = new("identity_session", "session_id"),
            [IdentityMethod("RevokeAllUserSessions")] = new("identity_session", "user_id"),
            [IdentityMethod("CreateClient")] = new("identity_client", null),
            [IdentityMethod("UpdateClient")] = new("identity_client", "client_id"),
            [IdentityMethod("RotateClientSecret")] = new("identity_client", "client_id"),
            [IdentityMethod("DeleteClient")] = new("identity_client", "client_id"),
            [IdentityMethod("CreateScope")] = new("identity_scope", null),
            [IdentityMethod("UpdateScope")] = new("identity_scope", "scope_id"),
            [IdentityMethod("DeleteScope")] = new("identity_scope", "scope_id"),
            [TargetingMethod("CreateSegment")] = new("targeting_segment", null),
            [TargetingMethod("UpdateSegment")] = new("targeting_segment", "segment_id"),
            [TargetingMethod("ArchiveSegment")] = new("targeting_segment", "segment_id"),
            [TargetingMethod("RestoreSegment")] = new("targeting_segment", "segment_id"),
            [FeatureMethod("CreateFlag")] = new("feature_flag", null),
            [FeatureMethod("UpdateFlagDraft")] = new("feature_flag", "flag_id"),
            [FeatureMethod("PublishFlag")] = new("feature_flag", "flag_id"),
            [FeatureMethod("RollbackFlag")] = new("feature_flag", "flag_id"),
            [FeatureMethod("ArchiveFlag")] = new("feature_flag", "flag_id"),
            [FeatureMethod("RestoreFlag")] = new("feature_flag", "flag_id"),
            [ConfigMethod("CreateConfigEntry")] = new("config_entry", null),
            [ConfigMethod("UpdateConfigDraft")] = new("config_entry", "entry_id"),
            [ConfigMethod("PublishConfigEntry")] = new("config_entry", "entry_id"),
            [ConfigMethod("RollbackConfigEntry")] = new("config_entry", "entry_id"),
            [ConfigMethod("ArchiveConfigEntry")] = new("config_entry", "entry_id"),
            [ConfigMethod("RestoreConfigEntry")] = new("config_entry", "entry_id"),
            [StorageMethod("CreateBucket")] = new("storage_bucket", null),
            [StorageMethod("UpdateBucket")] = new("storage_bucket", "bucket_id"),
            [StorageMethod("ArchiveBucket")] = new("storage_bucket", "bucket_id"),
            [StorageMethod("RestoreBucket")] = new("storage_bucket", "bucket_id"),
            [StorageMethod("UpdateObjectMetadata")] = new("storage_object", "object_id"),
            [StorageMethod("CreateUploadSession")] = new("storage_upload_session", null),
            [StorageMethod("CompleteUpload")] = new("storage_upload_session", "upload_session_id"),
            [StorageMethod("CreateDownloadUrl")] = new("storage_download_ticket", "object_id"),
            [StorageMethod("CopyObject")] = new("storage_object", "object_id"),
            [StorageMethod("DeleteObject")] = new("storage_object", "object_id"),
            [ReleaseMethod("CreateSigningKey")] = new("release_signing_key", null),
            [ReleaseMethod("ArchiveSigningKey")] = new("release_signing_key", "signing_key_id"),
            [ReleaseMethod("RestoreSigningKey")] = new("release_signing_key", "signing_key_id"),
            [ReleaseMethod("CreateChannel")] = new("release_channel", null),
            [ReleaseMethod("UpdateChannel")] = new("release_channel", "channel_id"),
            [ReleaseMethod("ArchiveChannel")] = new("release_channel", "channel_id"),
            [ReleaseMethod("RestoreChannel")] = new("release_channel", "channel_id"),
            [ReleaseMethod("CreateArtifactUpload")] = new("release_artifact", null),
            [ReleaseMethod("CompleteArtifactUpload")] = new("release_artifact", "artifact_id"),
            [ReleaseMethod("ArchiveArtifact")] = new("release_artifact", "artifact_id"),
            [ReleaseMethod("CreateRelease")] = new("desktop_release", null),
            [ReleaseMethod("UpdateReleaseDraft")] = new("desktop_release", "release_id"),
            [ReleaseMethod("PublishRelease")] = new("desktop_release", "release_id"),
            [ReleaseMethod("PauseRelease")] = new("desktop_release", "release_id"),
            [ReleaseMethod("PromoteRelease")] = new("desktop_release", "release_id"),
            [ReleaseMethod("RollbackRelease")] = new("desktop_release", "release_id"),
        };

    private static readonly Action<ILogger, string, string, Exception?> LogAuditFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Critical,
            new EventId(1101, nameof(LogAuditFailure)),
            "Could not persist audit event for {GrpcMethod}. Request ID: {RequestId}.");

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (!IsAuditable(context.Method))
        {
            return await continuation(request, context);
        }

        try
        {
            var response = await continuation(request, context);
            await AppendAsync(
                request,
                response,
                context,
                AuditOutcome.Succeeded,
                string.Empty);
            return response;
        }
        catch (Exception exception)
        {
            var (outcome, errorCode) = Classify(exception);
            await AppendAsync(request, null, context, outcome, errorCode);
            throw;
        }
    }

    private async Task AppendAsync<TRequest>(
        TRequest request,
        object? response,
        ServerCallContext context,
        AuditOutcome outcome,
        string errorCode)
    {
        var requestMessage = request as IMessage;
        var responseMessage = response as IMessage;
        var requestScope = ReadScope(requestMessage);
        var responseScope = ReadScope(responseMessage);
        var resource = Resources.GetValueOrDefault(context.Method)
            ?? InferResource(context.Method);
        var resourceId = resource.RequestIdField is null
            ? string.Empty
            : ReadString(requestMessage, resource.RequestIdField);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            resourceId = ReadString(responseMessage, "id");
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            resourceId = ReadString(ReadMessage(responseMessage, "user"), "id");
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            resourceId = ReadString(ReadMessage(responseMessage, "client"), "id");
        }

        var principal = context.GetHttpContext().User;
        var auditEvent = new AsterloomAuditEvent(
            Guid.CreateVersion7(),
            principal.FindFirstValue("sub")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            requestScope.TenantId ?? responseScope.TenantId,
            requestScope.ApplicationId ?? responseScope.ApplicationId,
            requestScope.EnvironmentId ?? responseScope.EnvironmentId,
            context.Method,
            resource.ResourceType,
            resourceId,
            context.GetHttpContext().TraceIdentifier,
            outcome,
            errorCode,
            CreateChangeSummary(requestMessage),
            timeProvider.GetUtcNow());

        try
        {
            await store.AppendAsync(auditEvent, CancellationToken.None);
        }
        catch (Exception auditException)
        {
            LogAuditFailure(
                logger,
                context.Method,
                context.GetHttpContext().TraceIdentifier,
                auditException);
        }
    }

    private static bool IsAuditable(string method)
    {
        if (!method.Contains(".admin.", StringComparison.Ordinal))
        {
            return false;
        }

        var methodName = method[(method.LastIndexOf('/') + 1)..];
        return AuditableMethodPrefixes.Any(prefix =>
            methodName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static (AuditOutcome Outcome, string ErrorCode) Classify(Exception exception)
    {
        if (exception is AsterloomException asterloomException)
        {
            return (
                asterloomException.Kind is AsterloomErrorKind.PermissionDenied
                    or AsterloomErrorKind.Unauthenticated
                    ? AuditOutcome.Denied
                    : AuditOutcome.Failed,
                asterloomException.ErrorCode);
        }

        if (exception is RpcException rpcException)
        {
            var errorCode = rpcException.Trailers.GetValue("x-asterloom-error-code")
                ?? rpcException.StatusCode.ToString();
            return (
                rpcException.StatusCode is StatusCode.PermissionDenied
                    or StatusCode.Unauthenticated
                    ? AuditOutcome.Denied
                    : AuditOutcome.Failed,
                errorCode);
        }

        return exception is OperationCanceledException
            ? (AuditOutcome.Failed, "request_cancelled")
            : (AuditOutcome.Failed, "internal_error");
    }

    private static AuditScope ReadScope(IMessage? message)
    {
        var scopeMessage = ReadMessage(message, "scope")
            ?? ReadMessage(ReadMessage(message, "input"), "scope");
        var source = scopeMessage ?? message;
        return new AuditScope(
            ParseId(ReadString(source, "tenant_id")),
            ParseId(ReadString(source, "application_id")),
            ParseId(ReadString(source, "environment_id")));
    }

    private static IMessage? ReadMessage(IMessage? message, string fieldName)
    {
        var field = message?.Descriptor.FindFieldByName(fieldName);
        return field?.Accessor.GetValue(message!) as IMessage;
    }

    private static string ReadString(IMessage? message, string fieldName)
    {
        var field = message?.Descriptor.FindFieldByName(fieldName);
        return field?.Accessor.GetValue(message!) as string ?? string.Empty;
    }

    private static Guid? ParseId(string value) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    private static string CreateChangeSummary(IMessage? request)
    {
        if (request is null)
        {
            return "request_fields=[]";
        }

        var fieldNames = request.Descriptor.Fields
            .InDeclarationOrder()
            .Select(static field => field.Name);
        return $"request_fields=[{string.Join(',', fieldNames)}]";
    }

    private static AuditResourceDescriptor InferResource(string method)
    {
        var methodName = method[(method.LastIndexOf('/') + 1)..];
        var prefix = AuditableMethodPrefixes.FirstOrDefault(candidate =>
            methodName.StartsWith(candidate, StringComparison.Ordinal));
        var resourceType = prefix is null
            ? "admin_operation"
            : ToSnakeCase(methodName[prefix.Length..]);
        return new AuditResourceDescriptor(resourceType, null);
    }

    private static string ToSnakeCase(string value)
    {
        var output = new System.Text.StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
            {
                output.Append('_');
            }

            output.Append(char.ToLowerInvariant(value[index]));
        }

        return output.Length == 0 ? "admin_operation" : output.ToString();
    }

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

    private static string FeatureMethod(string method) =>
        $"/asterloom.feature.admin.v1.FeatureAdminService/{method}";

    private static string ConfigMethod(string method) =>
        $"/asterloom.config.admin.v1.ConfigAdminService/{method}";

    private static string StorageMethod(string method) =>
        $"/asterloom.storage.admin.v1.StorageAdminService/{method}";

    private static string ReleaseMethod(string method) =>
        $"/asterloom.release.admin.v1.ReleaseAdminService/{method}";

    private sealed record AuditResourceDescriptor(
        string ResourceType,
        string? RequestIdField);

    private sealed record AuditScope(
        Guid? TenantId,
        Guid? ApplicationId,
        Guid? EnvironmentId);
}
