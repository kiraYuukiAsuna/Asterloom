using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Feature.Persistence;
using Asterloom.Modules.Outbox;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Targeting;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Feature;

public sealed partial class FeatureManagementService(
    IFeatureStore store,
    IPlatformResourceStore platformStore,
    FeatureDefinitionValidator validator,
    FeatureEvaluationService evaluator,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    public async Task<FeatureListResult<FeatureFlag>> ListFlagsAsync(
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
        var result = await store.ListFlagsAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<FeatureFlag> GetFlagAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireFlagAsync(scope, ParseId(flagId, "flagId"), cancellationToken);
    }

    public async Task<FeatureFlag> CreateFlagAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string? description,
        FeatureValueKind valueKind,
        FeatureDefinition definition,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        RequireValueKind(valueKind);
        var normalizedDefinition = NormalizeDraft(
            definition,
            fallbackSalt: CreateSalt());
        var now = timeProvider.GetUtcNow();
        var flag = new FeatureFlag(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            NormalizeKey(key),
            NormalizeDisplayName(displayName),
            NormalizeDescription(description),
            valueKind,
            FeatureResourceStatus.Active,
            normalizedDefinition,
            DraftRevision: 1,
            PublishedDefinition: null,
            PublishedRevision: null,
            Version: 1,
            now,
            now,
            ArchivedAt: null,
            PublishedAt: null);
        if (!await store.TryCreateFlagAsync(flag, cancellationToken))
        {
            throw AlreadyExists(
                "feature_flag_key_exists",
                "A feature flag with this key already exists in the environment.");
        }

        return flag;
    }

    public async Task<FeatureFlag> UpdateDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        string displayName,
        string? description,
        FeatureDefinition definition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
            DraftDefinition = NormalizeDraft(
                definition,
                current.DraftDefinition.BucketingSalt),
            DraftRevision = current.DraftRevision + 1,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateFlagAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<FeatureValidationResult> ValidateDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        CancellationToken cancellationToken)
    {
        var flag = await GetFlagAsync(
            tenantId,
            applicationId,
            environmentId,
            flagId,
            cancellationToken);
        return await validator.ValidateAsync(flag, flag.DraftDefinition, cancellationToken);
    }

    public async Task<FeatureFlag> PublishAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var validation = await validator.ValidateAsync(
            current,
            current.DraftDefinition,
            cancellationToken);
        RequireValid(validation);

        var now = timeProvider.GetUtcNow();
        var revisionNumber = (current.PublishedRevision ?? 0) + 1;
        var revision = new FeatureRevision(
            Guid.CreateVersion7(),
            current.Id,
            current.TenantId,
            current.ApplicationId,
            current.EnvironmentId,
            revisionNumber,
            current.DraftDefinition,
            SourceRevision: null,
            now);
        var updated = current with
        {
            PublishedDefinition = current.DraftDefinition,
            PublishedRevision = revisionNumber,
            PublishedAt = now,
            Version = current.Version + 1,
            UpdatedAt = now,
        };
        var integrationEvent = CreatePublishedEvent(
            updated,
            revision,
            validation.DefinitionHash,
            correlationId,
            now);
        if (!await store.TryPublishAsync(
                updated,
                current.Version,
                revision,
                integrationEvent,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<FeatureListResult<FeatureRevision>> ListRevisionsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var flag = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
            cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeArchived: true);
        var result = await store.ListRevisionsAsync(
            flag.Id,
            page.Offset,
            page.PageSize,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<FeatureFlag> RollbackAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        long targetRevision,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (targetRevision <= 0)
        {
            throw Invalid("revision", "Revision must be positive.");
        }

        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var target = await store.GetRevisionAsync(current.Id, targetRevision, cancellationToken)
            ?? throw NotFound(
                "feature_revision_not_found",
                "The requested published revision was not found.");
        var candidate = current with { DraftDefinition = target.Definition };
        var validation = await validator.ValidateAsync(
            candidate,
            target.Definition,
            cancellationToken);
        RequireValid(validation);

        var now = timeProvider.GetUtcNow();
        var revisionNumber = (current.PublishedRevision ?? 0) + 1;
        var revision = new FeatureRevision(
            Guid.CreateVersion7(),
            current.Id,
            current.TenantId,
            current.ApplicationId,
            current.EnvironmentId,
            revisionNumber,
            target.Definition,
            SourceRevision: targetRevision,
            now);
        var updated = current with
        {
            DraftDefinition = target.Definition,
            DraftRevision = current.DraftRevision + 1,
            PublishedDefinition = target.Definition,
            PublishedRevision = revisionNumber,
            PublishedAt = now,
            Version = current.Version + 1,
            UpdatedAt = now,
        };
        var integrationEvent = CreatePublishedEvent(
            updated,
            revision,
            validation.DefinitionHash,
            correlationId,
            now);
        if (!await store.TryPublishAsync(
                updated,
                current.Version,
                revision,
                integrationEvent,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public Task<FeatureFlag> ArchiveAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            flagId,
            expectedVersion,
            FeatureResourceStatus.Archived,
            cancellationToken);

    public Task<FeatureFlag> RestoreAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            flagId,
            expectedVersion,
            FeatureResourceStatus.Active,
            cancellationToken);

    public async Task<FeatureEvaluationDetails> SimulateAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        bool useDraft,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var flag = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
            cancellationToken);
        if (useDraft)
        {
            var validation = await validator.ValidateAsync(
                flag,
                flag.DraftDefinition,
                cancellationToken);
            RequireValid(validation);
        }

        return await evaluator.EvaluateAsync(
            new FeatureEvaluationRequest(
                scope,
                flag.Key,
                flag.ValueKind,
                context,
                useDraft,
                flag.Id),
            cancellationToken);
    }

    private async Task<FeatureFlag> ChangeStatusAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string flagId,
        long expectedVersion,
        FeatureResourceStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(
            scope,
            requireActive: desiredStatus == FeatureResourceStatus.Active,
            cancellationToken);
        var current = await RequireFlagAsync(
            scope,
            ParseId(flagId, "flagId"),
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
            ArchivedAt = desiredStatus == FeatureResourceStatus.Archived ? now : null,
        };
        if (!await store.TryUpdateFlagAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task RequireScopeAsync(
        FeatureScope scope,
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
                "feature_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private async Task<FeatureFlag> RequireFlagAsync(
        FeatureScope scope,
        Guid flagId,
        CancellationToken cancellationToken) =>
        await store.GetFlagAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            flagId,
            cancellationToken)
        ?? throw NotFound("feature_flag_not_found", "The feature flag was not found.");

    private static FeatureDefinition NormalizeDraft(
        FeatureDefinition definition,
        string fallbackSalt)
    {
        if (definition is null)
        {
            throw Invalid("definition", "A feature definition is required.");
        }

        var salt = string.IsNullOrWhiteSpace(definition.BucketingSalt)
            ? fallbackSalt
            : definition.BucketingSalt.Trim();
        var normalized = definition with { BucketingSalt = salt };
        try
        {
            FeatureDefinitionValidator.EnsureDraftSafety(normalized);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("definition", exception.Message);
        }

        return normalized;
    }

    private static void RequireValid(FeatureValidationResult validation)
    {
        if (validation.Valid)
        {
            return;
        }

        var messages = validation.Issues
            .Where(static issue => issue.Severity == FeatureValidationSeverity.Error)
            .Take(20)
            .GroupBy(static issue => issue.Path, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group.Select(issue => issue.Message).ToArray(),
                StringComparer.Ordinal);
        throw new AsterloomException(
            AsterloomErrorKind.FailedPrecondition,
            "feature_draft_invalid",
            "The feature draft must pass validation before this operation.",
            messages);
    }

    private static OutboxMessageDraft CreatePublishedEvent(
        FeatureFlag flag,
        FeatureRevision revision,
        string definitionHash,
        string correlationId,
        DateTimeOffset now) =>
        OutboxMessageFactory.Create(
            "asterloom.feature.flag-published.v1",
            1,
            new FeaturePublishedEvent(
                flag.Id,
                flag.Key,
                revision.Revision,
                revision.SourceRevision,
                definitionHash),
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            now,
            flag.TenantId,
            flag.ApplicationId,
            flag.EnvironmentId);

    private static FeatureScope ParseScope(
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

    private static void RequireValueKind(FeatureValueKind valueKind)
    {
        if (!Enum.IsDefined(valueKind))
        {
            throw Invalid("valueKind", "A supported feature value kind is required.");
        }
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

    private static void RequireActive(FeatureResourceStatus status)
    {
        if (status != FeatureResourceStatus.Active)
        {
            throw FailedPrecondition(
                "feature_flag_archived",
                "The feature flag is archived and must be restored first.");
        }
    }

    private static FeaturePageRequest CreatePageRequest(
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

        return new FeaturePageRequest(offset, normalizedSize, normalizedQuery, includeArchived);
    }

    private static FeatureListResult<T> ToListResult<T>(
        FeatureStorePage<T> result,
        int offset) =>
        new(
            result.Items,
            result.HasMore
                ? WebEncoders.Base64UrlEncode(
                    Encoding.UTF8.GetBytes((offset + result.Items.Count).ToString(
                        CultureInfo.InvariantCulture)))
                : string.Empty);

    private static string CreateSalt() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

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
}
