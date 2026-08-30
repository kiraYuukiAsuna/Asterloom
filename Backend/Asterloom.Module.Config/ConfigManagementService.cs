using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Config.Persistence;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Outbox;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Asterloom.Modules.Targeting.Model;
using Asterloom.Modules.Targeting.Persistence;
using Asterloom.Targeting;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Config;

public sealed partial class ConfigManagementService(
    IConfigStore store,
    IPlatformResourceStore platformStore,
    ITargetingStore targetingStore,
    ConfigDefinitionValidator validator,
    ConfigEvaluationService evaluator,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<ConfigListResult<ConfigEntry>> ListEntriesAsync(
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
        var result = await store.ListEntriesAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            page,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<ConfigEntry> GetEntryAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        return await RequireEntryAsync(scope, ParseId(entryId, "entryId"), cancellationToken);
    }

    public async Task<ConfigEntry> CreateEntryAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string? description,
        ConfigValueKind valueKind,
        ConfigVisibility visibility,
        ConfigDefinition definition,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        RequireValueKind(valueKind);
        RequireVisibility(visibility);
        var normalized = NormalizeDraft(definition);
        var now = timeProvider.GetUtcNow();
        var entry = new ConfigEntry(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            NormalizeKey(key),
            NormalizeDisplayName(displayName),
            NormalizeDescription(description),
            valueKind,
            visibility,
            ConfigResourceStatus.Active,
            normalized,
            DraftRevision: 1,
            PublishedDefinition: null,
            PublishedRevision: null,
            PublishedSnapshotVersion: null,
            Version: 1,
            now,
            now,
            ArchivedAt: null,
            PublishedAt: null);
        if (!await store.TryCreateEntryAsync(entry, cancellationToken))
        {
            throw AlreadyExists(
                "config_entry_key_exists",
                "A configuration entry with this key already exists in the environment.");
        }

        return entry;
    }

    public async Task<ConfigEntry> UpdateDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        string displayName,
        string? description,
        ConfigVisibility visibility,
        ConfigDefinition definition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        RequireVisibility(visibility);
        var current = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var updated = current with
        {
            DisplayName = NormalizeDisplayName(displayName),
            Description = NormalizeDescription(description),
            Visibility = visibility,
            DraftDefinition = NormalizeDraft(definition),
            DraftRevision = current.DraftRevision + 1,
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpdateEntryAsync(updated, current.Version, cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<ConfigValidationResult> ValidateDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(
            tenantId,
            applicationId,
            environmentId,
            entryId,
            cancellationToken);
        return await validator.ValidateAsync(entry, entry.DraftDefinition, cancellationToken);
    }

    public async Task<ConfigDiff> DiffDraftAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        CancellationToken cancellationToken)
    {
        var entry = await GetEntryAsync(
            tenantId,
            applicationId,
            environmentId,
            entryId,
            cancellationToken);
        var publishedJson = entry.PublishedDefinition is null
            ? "null"
            : JsonSerializer.Serialize(entry.PublishedDefinition, SerializerOptions);
        var draftJson = JsonSerializer.Serialize(entry.DraftDefinition, SerializerOptions);
        using var publishedDocument = JsonDocument.Parse(publishedJson);
        using var draftDocument = JsonDocument.Parse(draftJson);
        var paths = new List<string>();
        FindChangedPaths(
            publishedDocument.RootElement,
            draftDocument.RootElement,
            string.Empty,
            paths);
        return new(paths.Count > 0, publishedJson, draftJson, paths);
    }

    public async Task<ConfigEntry> PublishAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: true, cancellationToken);
        var current = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var validation = await validator.ValidateAsync(
            current,
            current.DraftDefinition,
            cancellationToken);
        RequireValid(validation);

        var now = timeProvider.GetUtcNow();
        var snapshotVersion = await NextSnapshotVersionAsync(scope, cancellationToken);
        var revisionNumber = (current.PublishedRevision ?? 0) + 1;
        var updated = current with
        {
            PublishedDefinition = current.DraftDefinition,
            PublishedRevision = revisionNumber,
            PublishedSnapshotVersion = snapshotVersion,
            PublishedAt = now,
            Version = current.Version + 1,
            UpdatedAt = now,
        };
        var revision = new ConfigRevision(
            Guid.CreateVersion7(),
            current.Id,
            current.TenantId,
            current.ApplicationId,
            current.EnvironmentId,
            revisionNumber,
            current.DraftDefinition,
            SourceRevision: null,
            snapshotVersion,
            now);
        var snapshot = await CreateSnapshotAsync(
            scope,
            updated,
            snapshotVersion,
            now,
            cancellationToken);
        var integrationEvent = CreateSnapshotEvent(
            updated,
            revision,
            validation.DefinitionHash,
            correlationId,
            now);
        if (!await store.TryCommitSnapshotAsync(
                updated,
                current.Version,
                revision,
                snapshot,
                integrationEvent,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public async Task<ConfigListResult<ConfigRevision>> ListRevisionsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var entry = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
            cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeArchived: true);
        var result = await store.ListRevisionsAsync(
            entry.Id,
            page.Offset,
            page.PageSize,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    public async Task<ConfigEntry> RollbackAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
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
        var current = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
            cancellationToken);
        RequireVersion(current.Version, expectedVersion);
        RequireActive(current.Status);
        var target = await store.GetRevisionAsync(current.Id, targetRevision, cancellationToken)
            ?? throw NotFound(
                "config_revision_not_found",
                "The requested configuration revision was not found.");
        var candidate = current with { DraftDefinition = target.Definition };
        var validation = await validator.ValidateAsync(
            candidate,
            target.Definition,
            cancellationToken);
        RequireValid(validation);

        var now = timeProvider.GetUtcNow();
        var snapshotVersion = await NextSnapshotVersionAsync(scope, cancellationToken);
        var revisionNumber = (current.PublishedRevision ?? 0) + 1;
        var updated = current with
        {
            DraftDefinition = target.Definition,
            DraftRevision = current.DraftRevision + 1,
            PublishedDefinition = target.Definition,
            PublishedRevision = revisionNumber,
            PublishedSnapshotVersion = snapshotVersion,
            PublishedAt = now,
            Version = current.Version + 1,
            UpdatedAt = now,
        };
        var revision = new ConfigRevision(
            Guid.CreateVersion7(),
            current.Id,
            current.TenantId,
            current.ApplicationId,
            current.EnvironmentId,
            revisionNumber,
            target.Definition,
            targetRevision,
            snapshotVersion,
            now);
        var snapshot = await CreateSnapshotAsync(
            scope,
            updated,
            snapshotVersion,
            now,
            cancellationToken);
        var integrationEvent = CreateSnapshotEvent(
            updated,
            revision,
            validation.DefinitionHash,
            correlationId,
            now);
        if (!await store.TryCommitSnapshotAsync(
                updated,
                current.Version,
                revision,
                snapshot,
                integrationEvent,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    public Task<ConfigEntry> ArchiveAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            entryId,
            expectedVersion,
            correlationId,
            ConfigResourceStatus.Archived,
            cancellationToken);

    public Task<ConfigEntry> RestoreAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        long expectedVersion,
        string correlationId,
        CancellationToken cancellationToken) =>
        ChangeStatusAsync(
            tenantId,
            applicationId,
            environmentId,
            entryId,
            expectedVersion,
            correlationId,
            ConfigResourceStatus.Active,
            cancellationToken);

    public async Task<ConfigEffectiveValue> PreviewAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        bool useDraft,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var entry = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
            cancellationToken);
        if (useDraft)
        {
            RequireValid(await validator.ValidateAsync(
                entry,
                entry.DraftDefinition,
                cancellationToken));
        }

        return await evaluator.PreviewAsync(entry, useDraft, context, cancellationToken);
    }

    public async Task<ConfigListResult<ConfigSnapshot>> ListSnapshotsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(scope, requireActive: false, cancellationToken);
        var page = CreatePageRequest(pageSize, pageToken, query: null, includeArchived: true);
        var result = await store.ListSnapshotsAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            page.Offset,
            page.PageSize,
            cancellationToken);
        return ToListResult(result, page.Offset);
    }

    private async Task<ConfigEntry> ChangeStatusAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string entryId,
        long expectedVersion,
        string correlationId,
        ConfigResourceStatus desiredStatus,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        await RequireScopeAsync(
            scope,
            requireActive: desiredStatus == ConfigResourceStatus.Active,
            cancellationToken);
        var current = await RequireEntryAsync(
            scope,
            ParseId(entryId, "entryId"),
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
            ArchivedAt = desiredStatus == ConfigResourceStatus.Archived ? now : null,
        };
        var publishedDefinition = current.PublishedDefinition;
        if (publishedDefinition is null)
        {
            if (!await store.TryUpdateEntryAsync(updated, current.Version, cancellationToken))
            {
                throw VersionConflict();
            }

            return updated;
        }

        var snapshotVersion = await NextSnapshotVersionAsync(scope, cancellationToken);
        updated = updated with { PublishedSnapshotVersion = snapshotVersion };
        var snapshot = await CreateSnapshotAsync(
            scope,
            updated,
            snapshotVersion,
            now,
            cancellationToken);
        var syntheticRevision = new ConfigRevision(
            Guid.CreateVersion7(),
            updated.Id,
            updated.TenantId,
            updated.ApplicationId,
            updated.EnvironmentId,
            updated.PublishedRevision!.Value,
            publishedDefinition,
            SourceRevision: null,
            snapshotVersion,
            now);
        var integrationEvent = CreateSnapshotEvent(
            updated,
            syntheticRevision,
            ConfigDefinitionValidator.ComputeHash(publishedDefinition),
            correlationId,
            now);
        if (!await store.TryCommitSnapshotAsync(
                updated,
                current.Version,
                revision: null,
                snapshot,
                integrationEvent,
                cancellationToken))
        {
            throw VersionConflict();
        }

        return updated;
    }

    private async Task<long> NextSnapshotVersionAsync(
        ConfigScope scope,
        CancellationToken cancellationToken)
    {
        var latest = await store.GetLatestSnapshotAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            cancellationToken);
        return (latest?.Version ?? 0) + 1;
    }

    private async Task<ConfigSnapshot> CreateSnapshotAsync(
        ConfigScope scope,
        ConfigEntry changedEntry,
        long snapshotVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entries = (await store.ListPublishedEntriesAsync(
                scope.TenantId,
                scope.ApplicationId,
                scope.EnvironmentId,
                cancellationToken))
            .Where(entry => entry.Id != changedEntry.Id)
            .Append(changedEntry)
            .Where(entry => entry.Status == ConfigResourceStatus.Active
                && entry.PublishedDefinition is not null
                && entry.PublishedRevision is not null)
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        var items = new List<ConfigSnapshotItem>(entries.Length);
        foreach (var entry in entries)
        {
            var capturedRules = new List<ConfigSnapshotTargetingRule>();
            foreach (var rule in entry.PublishedDefinition!.TargetingRules)
            {
                var segment = await targetingStore.GetSegmentAsync(
                    entry.TenantId,
                    entry.ApplicationId,
                    entry.EnvironmentId,
                    rule.SegmentId,
                    cancellationToken);
                if (segment?.Status == TargetingResourceStatus.Active)
                {
                    capturedRules.Add(new(
                        rule.Id,
                        rule.SegmentId,
                        segment.Version,
                        rule.Value,
                        segment.Rule));
                }
            }

            items.Add(new(
                entry.Id,
                entry.Key,
                entry.ValueKind,
                entry.Visibility,
                entry.PublishedRevision!.Value,
                entry.PublishedDefinition,
                capturedRules));
        }

        return new(
            Guid.CreateVersion7(),
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            snapshotVersion,
            items,
            now);
    }

    private async Task RequireScopeAsync(
        ConfigScope scope,
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
                "config_scope_archived",
                "The tenant, application, and environment must all be active.");
        }
    }

    private async Task<ConfigEntry> RequireEntryAsync(
        ConfigScope scope,
        Guid entryId,
        CancellationToken cancellationToken) =>
        await store.GetEntryAsync(
            scope.TenantId,
            scope.ApplicationId,
            scope.EnvironmentId,
            entryId,
            cancellationToken)
        ?? throw NotFound("config_entry_not_found", "The configuration entry was not found.");

    private static ConfigDefinition NormalizeDraft(ConfigDefinition definition)
    {
        if (definition is null)
        {
            throw Invalid("definition", "A configuration definition is required.");
        }

        var normalized = definition with { SchemaJson = definition.SchemaJson.Trim() };
        try
        {
            ConfigDefinitionValidator.EnsureDraftSafety(normalized);
        }
        catch (ArgumentException exception)
        {
            throw Invalid("definition", exception.Message);
        }

        return normalized;
    }

    private static void RequireValid(ConfigValidationResult validation)
    {
        if (validation.Valid)
        {
            return;
        }

        var fields = validation.Issues
            .Where(static issue => issue.Severity == ConfigValidationSeverity.Error)
            .Take(20)
            .GroupBy(static issue => issue.Path, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group.Select(item => item.Message).ToArray(),
                StringComparer.Ordinal);
        throw new AsterloomException(
            AsterloomErrorKind.FailedPrecondition,
            "config_draft_invalid",
            "The configuration draft must pass validation before this operation.",
            fields);
    }

    private static OutboxMessageDraft CreateSnapshotEvent(
        ConfigEntry entry,
        ConfigRevision revision,
        string definitionHash,
        string correlationId,
        DateTimeOffset now) =>
        OutboxMessageFactory.Create(
            "asterloom.config.snapshot-published.v1",
            1,
            new ConfigPublishedEvent(
                entry.Id,
                entry.Key,
                revision.Revision,
                revision.SnapshotVersion,
                revision.SourceRevision,
                definitionHash),
            string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId,
            now,
            entry.TenantId,
            entry.ApplicationId,
            entry.EnvironmentId);

    private static void FindChangedPaths(
        JsonElement left,
        JsonElement right,
        string path,
        List<string> changedPaths)
    {
        if (left.ValueKind != right.ValueKind)
        {
            changedPaths.Add(string.IsNullOrEmpty(path) ? "/" : path);
            return;
        }

        if (left.ValueKind == JsonValueKind.Object)
        {
            var names = left.EnumerateObject().Select(static item => item.Name)
                .Concat(right.EnumerateObject().Select(static item => item.Name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal);
            foreach (var name in names)
            {
                var childPath = $"{path}/{EscapePointer(name)}";
                if (!left.TryGetProperty(name, out var leftValue)
                    || !right.TryGetProperty(name, out var rightValue))
                {
                    changedPaths.Add(childPath);
                }
                else
                {
                    FindChangedPaths(leftValue, rightValue, childPath, changedPaths);
                }
            }
            return;
        }

        if (left.ValueKind == JsonValueKind.Array)
        {
            if (left.GetArrayLength() != right.GetArrayLength())
            {
                changedPaths.Add(string.IsNullOrEmpty(path) ? "/" : path);
                return;
            }

            var leftItems = left.EnumerateArray().ToArray();
            var rightItems = right.EnumerateArray().ToArray();
            for (var index = 0; index < leftItems.Length; index++)
            {
                FindChangedPaths(leftItems[index], rightItems[index], $"{path}/{index}", changedPaths);
            }
            return;
        }

        if (!JsonElement.DeepEquals(left, right))
        {
            changedPaths.Add(string.IsNullOrEmpty(path) ? "/" : path);
        }
    }

    private static string EscapePointer(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static ConfigScope ParseScope(
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

    private static void RequireValueKind(ConfigValueKind valueKind)
    {
        if (!Enum.IsDefined(valueKind))
        {
            throw Invalid("valueKind", "A supported configuration value kind is required.");
        }
    }

    private static void RequireVisibility(ConfigVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
        {
            throw Invalid("visibility", "Configuration visibility must be Client or Server.");
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

    private static void RequireActive(ConfigResourceStatus status)
    {
        if (status != ConfigResourceStatus.Active)
        {
            throw FailedPrecondition(
                "config_entry_archived",
                "The configuration entry is archived and must be restored first.");
        }
    }

    private static ConfigPageRequest CreatePageRequest(
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
        return new(offset, normalizedSize, normalizedQuery, includeArchived);
    }

    private static ConfigListResult<T> ToListResult<T>(
        ConfigStorePage<T> result,
        int offset) =>
        new(
            result.Items,
            result.HasMore
                ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                    (offset + result.Items.Count).ToString(CultureInfo.InvariantCulture)))
                : string.Empty);

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
