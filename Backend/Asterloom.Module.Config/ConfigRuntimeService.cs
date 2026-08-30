using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Targeting;

namespace Asterloom.Modules.Config;

public sealed class ConfigRuntimeService(
    IPlatformResourceStore platformStore,
    ConfigEvaluationService evaluator)
{
    public async Task<ConfigSnapshotResult> GetSnapshotAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        TargetingEvaluationContext context,
        string? ifNoneMatch,
        bool includeServerValues,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireActiveScopeAsync(scope, cancellationToken);
        return await evaluator.GetSnapshotAsync(
            scope,
            context,
            ifNoneMatch,
            includeServerValues,
            cancellationToken);
    }

    public async Task<ConfigUpdateStatus> CheckUpdatesAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        long knownSnapshotVersion,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireActiveScopeAsync(scope, cancellationToken);
        return await evaluator.CheckUpdatesAsync(
            scope,
            knownSnapshotVersion,
            context,
            cancellationToken);
    }

    private async Task RequireActiveScopeAsync(
        ConfigScope scope,
        CancellationToken cancellationToken)
    {
        var tenant = await platformStore.GetTenantAsync(scope.TenantId, cancellationToken)
            ?? throw NotFound("tenant_not_found", "The tenant was not found.");
        var application = await platformStore.GetApplicationAsync(
            scope.TenantId,
            scope.ApplicationId,
            cancellationToken)
            ?? throw NotFound("application_not_found", "The application was not found.");
        var environment = await platformStore.GetEnvironmentAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            cancellationToken)
            ?? throw NotFound("environment_not_found", "The environment was not found.");
        if (tenant.Status != PlatformResourceStatus.Active
            || application.Status != PlatformResourceStatus.Active
            || environment.Status != PlatformResourceStatus.Active)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "config_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private static ConfigScope ParseScope(
        string tenantId,
        string applicationId,
        string environmentId) =>
        new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    public static Guid ParseId(string value, string field)
    {
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id;
        }
        throw new AsterloomException(
            AsterloomErrorKind.InvalidArgument,
            "validation_failed",
            "One or more fields are invalid.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [field] = ["A valid identifier is required."],
            });
    }

    private static AsterloomException NotFound(string code, string message) =>
        new(AsterloomErrorKind.NotFound, code, message);
}
