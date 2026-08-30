using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Targeting;

public sealed partial class TargetingManagementService(
    ITargetingStore store,
    IPlatformResourceStore platformStore,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    private static readonly TargetingCatalog Catalog = CreateCatalog();

    public static TargetingCatalog GetCatalog() => Catalog;

    public async Task<TargetingListResult<TargetingSegment>> ListSegmentsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var result = await store.ListSegmentsAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<TargetingSegment> GetSegmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireSegmentAsync(
            scope,
            ParseId(segmentId, "segmentId"),
            cancellationToken);
    }

    public async Task<TargetingSegment> CreateSegmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string? description,
        TargetingRule rule,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var segment = new TargetingSegment(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            NormalizeKey(key),
            NormalizeDisplayName(displayName),
            NormalizeDescription(description),
            ValidateRule(rule),
            TargetingResourceStatus.Active,
            Version: 1,
            now,
            now,
            ArchivedAt: null);
        if (!await store.TryCreateSegmentAsync(segment, cancellationToken))
        {
            throw AlreadyExists(
                "targeting_segment_key_exists",
                "A targeting segment with this key already exists in the environment.");
        }

        return segment;
    }

    public async Task<TargetingSegment> UpdateSegmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        string displayName,
        string? description,
        TargetingRule rule,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireSegmentAsync(
            scope,
            ParseId(segmentId, "segmentId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
            Rule = ValidateRule(rule),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateSegmentAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public Task<TargetingSegment> ArchiveSegmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            segmentId,
            expectedVersion,
            TargetingResourceStatus.Archived,
            cancellationToken);

    public Task<TargetingSegment> RestoreSegmentAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            segmentId,
            expectedVersion,
            TargetingResourceStatus.Active,
            cancellationToken);

    public async Task<TargetingSimulationOutcome> SimulateAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        TargetingEvaluationContext evaluationContext,
        TargetingBucketPreviewRequest? bucketPreview,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        if (evaluationContext.ApplicationId != scope.ApplicationId
            || evaluationContext.EnvironmentId != scope.EnvironmentId)
        {
            throw Invalid(
                "context",
                "The evaluation context must use the application and environment from the route.");
        }

        ValidateContext(evaluationContext);
        var segment = await RequireSegmentAsync(
            scope,
            ParseId(segmentId, "segmentId"),
            cancellationToken);
        var result = TargetingEvaluator.Evaluate(segment.Rule, evaluationContext);

        var bucketEvaluated = bucketPreview is not null;
        uint bucket = 0;
        var bucketNamespace = string.Empty;
        var selectedVariant = string.Empty;
        if (bucketPreview is not null)
        {
            try
            {
                bucketNamespace = TargetingContract.CreateBucketNamespace(
                    bucketPreview.ResourceType,
                    bucketPreview.ResourceKey,
                    scope.EnvironmentId);
                bucket = TargetingContract.ComputeBucket(
                    bucketNamespace,
                    bucketPreview.Salt,
                    evaluationContext.TargetingKey);
                selectedVariant = TargetingContract.SelectBucketAllocation(
                    bucket,
                    bucketPreview.Allocations) ?? string.Empty;
            }
            catch (ArgumentException exception)
            {
                throw Invalid("bucketPreview", exception.Message);
            }
        }

        return new TargetingSimulationOutcome(
            segment.Id,
            segment.Key,
            segment.Version,
            result.Matched,
            result.Matched ? "segment_matched" : "segment_not_matched",
            result.Conditions,
            bucketEvaluated,
            bucket,
            selectedVariant,
            bucketNamespace,
            "v1");
    }

    private async Task<TargetingSegment> ChangeStatusAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string segmentId,
        long expectedVersion,
        TargetingResourceStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(
            scope,
            requireActive: desiredStatus == TargetingResourceStatus.Active,
            cancellationToken);
        var current = await RequireSegmentAsync(
            scope,
            ParseId(segmentId, "segmentId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        if (current.Status == desiredStatus)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = desiredStatus,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = desiredStatus == TargetingResourceStatus.Archived ? now : null,
        };
        if (!await store.TryUpdateSegmentAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task RequireScopeAsync(
        TargetingScope scope,
        bool requireActive,
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
        if (requireActive
            && (tenant.Status != PlatformResourceStatus.Active
                || application.Status != PlatformResourceStatus.Active
                || environment.Status != PlatformResourceStatus.Active))
        {
            throw FailedPrecondition(
                "targeting_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private async Task<TargetingSegment> RequireSegmentAsync(
        TargetingScope scope,
        Guid segmentId,
        CancellationToken cancellationToken) =>
        await store.GetSegmentAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            segmentId,
            cancellationToken)
        ?? throw NotFound("targeting_segment_not_found", "The targeting segment was not found.");

    private static TargetingRule ValidateRule(TargetingRule rule)
    {
        if (rule is null)
        {
            throw Invalid("rule", "A targeting rule is required.");
        }

        try
        {
            TargetingContract.ValidateRule(rule);
            return rule;
        }
        catch (ArgumentException exception)
        {
            throw Invalid("rule", exception.Message);
        }
    }

    private static void ValidateContext(TargetingEvaluationContext context)
    {
        if (context is null)
        {
            throw Invalid("context", "An evaluation context is required.");
        }

        try
        {
            TargetingContract.ValidateContext(context);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("context", exception.Message);
        }
    }

    private static TargetingScope ParseScope(
        string tenantId,
        string applicationId,
        string environmentId) =>
        new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    private static Guid ParseId(string value, string field)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw Invalid(field, "A valid identifier is required.");
        }

        return id;
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KeyPattern().IsMatch(normalized))
        {
            throw Invalid(
                "key",
                "Use 1-100 lowercase letters, numbers, periods, underscores, or hyphens; start and end with a letter or number.");
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200 || normalized.Any(char.IsControl))
        {
            throw Invalid(
                "displayName",
                "Display name must contain 1-200 characters without control characters.");
        }

        return normalized;
    }

    private static string NormalizeDescription(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > 1_000 || normalized.Any(char.IsControl))
        {
            throw Invalid(
                "description",
                "Description must not exceed 1000 characters or contain control characters.");
        }

        return normalized;
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

    private static void RequireActive(TargetingResourceStatus status)
    {
        if (status != TargetingResourceStatus.Active)
        {
            throw FailedPrecondition(
                "targeting_segment_archived",
                "The targeting segment is archived and must be restored first.");
        }
    }

    private static TargetingPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived)
    {
        var normalizedSize = pageSize == 0 ? DefaultPageSize : pageSize;
        if (normalizedSize is < 1 or > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(pageToken));
                if (!int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                    || offset < 0)
                {
                    throw new FormatException();
                }
            }
            catch (FormatException)
            {
                throw Invalid("pageToken", "Page token is invalid.");
            }
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length > 200 || normalizedQuery.Any(char.IsControl))
        {
            throw Invalid(
                "query",
                "Query must not exceed 200 characters or contain control characters.");
        }

        return new TargetingPageRequest(offset, normalizedSize, normalizedQuery, includeArchived);
    }

    private static TargetingListResult<T> ToListResult<T>(
        TargetingStorePage<T> result,
        int offset) =>
        new(
            result.Items,
            result.HasMore
                ? WebEncoders.Base64UrlEncode(
                    Encoding.UTF8.GetBytes((offset + result.Items.Count).ToString(
                        CultureInfo.InvariantCulture)))
                : string.Empty);

    private static TargetingCatalog CreateCatalog()
    {
        TargetingValueKind[] text = [TargetingValueKind.Text];
        TargetingValueKind[] scalar =
        [
            TargetingValueKind.Text,
            TargetingValueKind.Truth,
            TargetingValueKind.Numeric,
        ];
        return new TargetingCatalog(
            [
                new("targetingKey", "Targeting key", TargetingValueKind.Text, true, true),
                new("userId", "User ID", TargetingValueKind.Text, true, false),
                new("applicationId", "Application ID", TargetingValueKind.Text, true, true),
                new("environmentId", "Environment ID", TargetingValueKind.Text, true, true),
                new("clientVersion", "Client version", TargetingValueKind.Text, true, false),
                new("platform", "Platform", TargetingValueKind.Text, true, false),
                new("region", "Region", TargetingValueKind.Text, true, false),
                new("language", "Language", TargetingValueKind.Text, true, false),
            ],
            [
                new(TargetingOperator.Equals, "Equals", scalar, 1, 1),
                new(TargetingOperator.NotEquals, "Does not equal", scalar, 1, 1),
                new(TargetingOperator.OneOf, "Is one of", scalar, 1, 50),
                new(TargetingOperator.NotOneOf, "Is not one of", scalar, 1, 50),
                new(TargetingOperator.Contains, "Contains", text, 1, 1),
                new(TargetingOperator.StartsWith, "Starts with", text, 1, 1),
                new(TargetingOperator.EndsWith, "Ends with", text, 1, 1),
                new(TargetingOperator.GreaterThan, "Greater than", [TargetingValueKind.Numeric], 1, 1),
                new(TargetingOperator.GreaterThanOrEqual, "Greater than or equal", [TargetingValueKind.Numeric], 1, 1),
                new(TargetingOperator.LessThan, "Less than", [TargetingValueKind.Numeric], 1, 1),
                new(TargetingOperator.LessThanOrEqual, "Less than or equal", [TargetingValueKind.Numeric], 1, 1),
                new(TargetingOperator.Exists, "Exists", scalar, 0, 0),
                new(TargetingOperator.NotExists, "Does not exist", scalar, 0, 0),
                new(TargetingOperator.SemanticVersionEquals, "Semantic version equals", text, 1, 1),
                new(TargetingOperator.SemanticVersionGreaterThan, "Semantic version greater than", text, 1, 1),
                new(TargetingOperator.SemanticVersionLessThan, "Semantic version less than", text, 1, 1),
            ],
            MaximumCustomAttributes: 64,
            MaximumConditions: 50,
            BucketingVersion: "v1",
            BucketCount: TargetingContract.BucketCount);
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

    [GeneratedRegex(
        "^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    private sealed record TargetingScope(
        Guid TenantId,
        Guid ApplicationId,
        Guid EnvironmentId);
}
