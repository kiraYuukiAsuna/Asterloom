using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Asterloom.Modules.Analytics.Model;
using Asterloom.Modules.Analytics.Persistence;
using Asterloom.Modules.Errors;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Analytics;

public sealed class AnalyticsManagementService(IAnalyticsStore store, TimeProvider timeProvider)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int DefaultRetentionDays = 90;
    private const int MaximumExportRows = 10_000;
    private static readonly Regex KeyPattern = new(
        "^[a-z][a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<AnalyticsListResult<EventSchema>> ListEventSchemasAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var request = CreatePageRequest(pageSize, pageToken, query, includeArchived);
        var page = await store.ListEventSchemasAsync(
            ParseScope(tenantId, applicationId, environmentId),
            request,
            cancellationToken);
        return new(
            page.Items,
            page.HasMore ? EncodeOffset(request.Offset + page.Items.Count) : string.Empty);
    }

    public async Task<EventSchema> GetEventSchemaAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string eventSchemaId,
        CancellationToken cancellationToken) =>
        await RequireSchemaAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(eventSchemaId, "eventSchemaId"),
            cancellationToken);

    public async Task<EventSchema> CreateEventSchemaAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string description,
        string schemaJson,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var normalizedKey = NormalizeKey(key);
        var now = timeProvider.GetUtcNow();
        var schema = new EventSchema(
            Guid.CreateVersion7(now),
            scope,
            normalizedKey,
            RequireText(displayName, "displayName", 200),
            NormalizeText(description, "description", 2_000),
            AnalyticsSchemaValidator.ValidateAndNormalizeSchema(schemaJson),
            AnalyticsResourceStatus.Active,
            ValidateRetention(retentionDays == 0 ? DefaultRetentionDays : retentionDays),
            1,
            now,
            now,
            null);

        if (!await store.TryCreateEventSchemaAsync(schema, cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.AlreadyExists,
                "analytics_schema_key_exists",
                "An event schema with the same key already exists in this environment.");
        }

        return schema;
    }

    public async Task<EventSchema> UpdateEventSchemaAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string eventSchemaId,
        string displayName,
        string description,
        string schemaJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var current = await RequireSchemaAsync(
            scope,
            ParseId(eventSchemaId, "eventSchemaId"),
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        EnsureActive(current);
        var updated = current with
        {
            DisplayName = RequireText(displayName, "displayName", 200),
            Description = NormalizeText(description, "description", 2_000),
            SchemaJson = AnalyticsSchemaValidator.ValidateAndNormalizeSchema(schemaJson),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await SaveSchemaAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public Task<EventSchema> ArchiveEventSchemaAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string eventSchemaId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeSchemaStatusAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(eventSchemaId, "eventSchemaId"),
            expectedVersion,
            AnalyticsResourceStatus.Archived,
            cancellationToken);

    public Task<EventSchema> RestoreEventSchemaAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string eventSchemaId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeSchemaStatusAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(eventSchemaId, "eventSchemaId"),
            expectedVersion,
            AnalyticsResourceStatus.Active,
            cancellationToken);

    public async Task<EventSchema> UpdateRetentionAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string eventSchemaId,
        int retentionDays,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var current = await RequireSchemaAsync(
            scope,
            ParseId(eventSchemaId, "eventSchemaId"),
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        var updated = current with
        {
            RetentionDays = ValidateRetention(retentionDays),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await SaveSchemaAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public Task<IReadOnlyList<AnalyticsWriteKey>> ListWriteKeysAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        bool includeRevoked,
        CancellationToken cancellationToken) => store.ListWriteKeysAsync(
            ParseScope(tenantId, applicationId, environmentId),
            includeRevoked,
            cancellationToken);

    public async Task<AnalyticsWriteKeyCredential> CreateWriteKeyAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string name,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var normalizedName = RequireText(name, "name", 200);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            var secret = CreateSecret(prefix);
            var now = timeProvider.GetUtcNow();
            var writeKey = new AnalyticsWriteKey(
                Guid.CreateVersion7(now),
                scope,
                normalizedName,
                prefix,
                HashSecret(secret),
                AnalyticsWriteKeyStatus.Active,
                1,
                now,
                now,
                null,
                null);
            if (await store.TryCreateWriteKeyAsync(writeKey, cancellationToken))
            {
                return new(writeKey, secret);
            }
        }

        throw new AsterloomException(
            AsterloomErrorKind.Conflict,
            "analytics_write_key_generation_failed",
            "A unique write key could not be generated. Retry the operation.");
    }

    public async Task<AnalyticsWriteKeyCredential> RotateWriteKeyAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string writeKeyId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var current = await RequireWriteKeyAsync(
            scope,
            ParseId(writeKeyId, "writeKeyId"),
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        if (current.Status != AnalyticsWriteKeyStatus.Active)
        {
            throw Failed("analytics_write_key_revoked", "A revoked write key cannot be rotated.");
        }

        var secret = CreateSecret(current.Prefix);
        var updated = current with
        {
            SecretHash = HashSecret(secret),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await SaveWriteKeyAsync(updated, current.Version, cancellationToken);
        return new(updated, secret);
    }

    public async Task<AnalyticsWriteKey> RevokeWriteKeyAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string writeKeyId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var current = await RequireWriteKeyAsync(
            scope,
            ParseId(writeKeyId, "writeKeyId"),
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        if (current.Status == AnalyticsWriteKeyStatus.Revoked)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = AnalyticsWriteKeyStatus.Revoked,
            Version = current.Version + 1,
            UpdatedAt = now,
            RevokedAt = now,
        };
        await SaveWriteKeyAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public async Task<AnalyticsListResult<StoredAnalyticsEvent>> ListEventsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? eventName,
        string? actorId,
        string? eventId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        CancellationToken cancellationToken)
    {
        var filter = CreateEventFilter(
            pageSize,
            pageToken,
            eventName,
            actorId,
            eventId,
            fromAt,
            toAt);
        var page = await store.ListEventsAsync(
            ParseScope(tenantId, applicationId, environmentId),
            filter,
            cancellationToken);
        return new(
            page.Items,
            page.HasMore ? EncodeOffset(filter.Offset + page.Items.Count) : string.Empty);
    }

    public async Task<StoredAnalyticsEvent> GetEventAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string analyticsEventId,
        CancellationToken cancellationToken) =>
        await store.GetEventAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(analyticsEventId, "analyticsEventId"),
            cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.NotFound,
            "analytics_event_not_found",
            "The analytics event was not found.");

    public Task<IReadOnlyList<AnalyticsAggregationBucket>> QueryAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        IReadOnlyList<string> eventNames,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        string interval,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = fromAt ?? now.AddDays(-7);
        var to = toAt ?? now;
        ValidateRange(from, to, maximumDays: 366);
        var names = eventNames
            .Select(static item => item.Trim().ToLowerInvariant())
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (names.Length > 20 || names.Any(static name => !KeyPattern.IsMatch(name)))
        {
            throw Invalid("eventNames", "At most 20 valid event names may be queried.");
        }

        var parsedInterval = (interval?.Trim().ToLowerInvariant()) switch
        {
            "hour" => AnalyticsInterval.Hour,
            "day" or "" => AnalyticsInterval.Day,
            "week" => AnalyticsInterval.Week,
            _ => throw Invalid("interval", "Interval must be hour, day, or week."),
        };
        return store.AggregateAsync(
            new AnalyticsAggregationQuery(
                ParseScope(tenantId, applicationId, environmentId),
                names,
                from,
                to,
                parsedInterval),
            cancellationToken);
    }

    public async Task<AnalyticsExportResult> ExportAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string? eventName,
        string? actorId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        var limit = maximumRows == 0 ? 1_000 : maximumRows;
        if (limit is < 1 or > MaximumExportRows)
        {
            throw Invalid("maximumRows", $"Maximum rows must be between 1 and {MaximumExportRows}.");
        }

        var scope = ParseScope(tenantId, applicationId, environmentId);
        var rows = new List<StoredAnalyticsEvent>(Math.Min(limit, 1_024));
        var offset = 0;
        while (rows.Count < limit)
        {
            var filter = CreateEventFilter(
                Math.Min(MaximumPageSize, limit - rows.Count),
                EncodeOffset(offset),
                eventName,
                actorId,
                null,
                fromAt,
                toAt);
            var page = await store.ListEventsAsync(scope, filter, cancellationToken);
            rows.AddRange(page.Items);
            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            offset += page.Items.Count;
        }

        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new(
            $"asterloom-analytics-{stamp}.csv",
            "text/csv; charset=utf-8",
            Encoding.UTF8.GetBytes(CreateCsv(rows)),
            rows.Count);
    }

    private async Task<EventSchema> ChangeSchemaStatusAsync(
        AnalyticsScope scope,
        Guid schemaId,
        long expectedVersion,
        AnalyticsResourceStatus status,
        CancellationToken cancellationToken)
    {
        var current = await RequireSchemaAsync(scope, schemaId, cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        if (current.Status == status)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = status,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = status == AnalyticsResourceStatus.Archived ? now : null,
        };
        await SaveSchemaAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    private async Task<EventSchema> RequireSchemaAsync(
        AnalyticsScope scope,
        Guid id,
        CancellationToken cancellationToken) =>
        await store.GetEventSchemaAsync(scope, id, cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.NotFound,
            "analytics_schema_not_found",
            "The event schema was not found.");

    private async Task<AnalyticsWriteKey> RequireWriteKeyAsync(
        AnalyticsScope scope,
        Guid id,
        CancellationToken cancellationToken) =>
        await store.GetWriteKeyAsync(scope, id, cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.NotFound,
            "analytics_write_key_not_found",
            "The analytics write key was not found.");

    private async Task SaveSchemaAsync(
        EventSchema schema,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryUpdateEventSchemaAsync(schema, expectedVersion, cancellationToken))
        {
            throw Conflict();
        }
    }

    private async Task SaveWriteKeyAsync(
        AnalyticsWriteKey writeKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryUpdateWriteKeyAsync(writeKey, expectedVersion, cancellationToken))
        {
            throw Conflict();
        }
    }

    private static AnalyticsPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeInactive)
    {
        var size = NormalizePageSize(pageSize);
        return new(
            DecodeOffset(pageToken),
            size,
            NormalizeText(query, "query", 200),
            includeInactive);
    }

    private static AnalyticsEventFilter CreateEventFilter(
        int pageSize,
        string? pageToken,
        string? eventName,
        string? actorId,
        string? eventId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt)
    {
        if (fromAt is not null && toAt is not null)
        {
            ValidateRange(fromAt.Value, toAt.Value, maximumDays: 3660);
        }

        return new(
            DecodeOffset(pageToken),
            NormalizePageSize(pageSize),
            NormalizeText(eventName, "eventName", 100).ToLowerInvariant(),
            NormalizeText(actorId, "actorId", 200),
            NormalizeText(eventId, "eventId", 128),
            fromAt,
            toAt);
    }

    private static int NormalizePageSize(int pageSize)
    {
        var size = pageSize == 0 ? DefaultPageSize : pageSize;
        return size is >= 1 and <= MaximumPageSize
            ? size
            : throw Invalid("pageSize", $"Page size must be between 1 and {MaximumPageSize}.");
    }

    internal static AnalyticsScope ParseScope(string tenantId, string applicationId, string environmentId) =>
        new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    internal static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw Invalid(field, "A valid identifier is required.");

    private static string NormalizeKey(string value)
    {
        var key = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return KeyPattern.IsMatch(key)
            ? key
            : throw Invalid(
                "key",
                "Key must start with a letter and contain 2-100 lowercase letters, digits, dots, underscores, or hyphens.");
    }

    internal static string RequireText(string? value, string field, int maximumLength)
    {
        var normalized = NormalizeText(value, field, maximumLength);
        return normalized.Length > 0
            ? normalized
            : throw Invalid(field, "A value is required.");
    }

    internal static string NormalizeText(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maximumLength
            ? normalized
            : throw Invalid(field, $"Value must not exceed {maximumLength} characters.");
    }

    private static int ValidateRetention(int value) => value is >= 1 and <= 3650
        ? value
        : throw Invalid("retentionDays", "Retention must be between 1 and 3650 days.");

    private static void EnsureActive(EventSchema schema)
    {
        if (schema.Status != AnalyticsResourceStatus.Active)
        {
            throw Failed("analytics_schema_archived", "An archived event schema cannot be edited.");
        }
    }

    private static void EnsureVersion(long expected, long actual)
    {
        if (expected <= 0 || expected != actual)
        {
            throw Conflict();
        }
    }

    private static void ValidateRange(DateTimeOffset from, DateTimeOffset to, int maximumDays)
    {
        if (from > to || to - from > TimeSpan.FromDays(maximumDays))
        {
            throw Invalid(
                "fromAt",
                $"Time range must be ordered and no longer than {maximumDays} days.");
        }
    }

    private static string CreateSecret(string prefix) =>
        $"ast_an_{prefix}_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";

    internal static byte[] HashSecret(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    internal static string? ExtractPrefix(string secret)
    {
        if (!secret.StartsWith("ast_an_", StringComparison.Ordinal))
        {
            return null;
        }

        var separator = secret.IndexOf('_', "ast_an_".Length);
        return separator > "ast_an_".Length
            ? secret["ast_an_".Length..separator]
            : null;
    }

    private static int DecodeOffset(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            if (int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                && offset >= 0)
            {
                return offset;
            }
        }
        catch (FormatException)
        {
        }

        throw Invalid("pageToken", "Page token is invalid.");
    }

    private static string EncodeOffset(int offset) => WebEncoders.Base64UrlEncode(
        Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static string CreateCsv(IEnumerable<StoredAnalyticsEvent> rows)
    {
        var output = new StringBuilder(
            "received_at,occurred_at,event_id,event_name,schema_version,actor_id,anonymous_id," +
            "session_id,sdk_name,sdk_version,write_key_prefix,properties_json,context_json\r\n");
        foreach (var row in rows)
        {
            AppendCsvRow(
                output,
                row.ReceivedAt.ToString("O", CultureInfo.InvariantCulture),
                row.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
                row.EventId,
                row.EventName,
                row.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                row.ActorId,
                row.AnonymousId,
                row.SessionId,
                row.SdkName,
                row.SdkVersion,
                row.WriteKeyPrefix,
                row.PropertiesJson,
                row.ContextJson);
        }

        return output.ToString();
    }

    private static void AppendCsvRow(StringBuilder output, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                output.Append(',');
            }

            var value = values[index];
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                value = "'" + value;
            }

            output.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        output.Append("\r\n");
    }

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "analytics_validation_failed",
        "One or more analytics fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });

    private static AsterloomException Failed(string code, string message) => new(
        AsterloomErrorKind.FailedPrecondition,
        code,
        message);

    private static AsterloomException Conflict() => new(
        AsterloomErrorKind.Conflict,
        "analytics_version_conflict",
        "The analytics resource changed. Refresh it and retry.");
}

public sealed record AnalyticsListResult<T>(IReadOnlyList<T> Items, string NextPageToken);

public sealed record AnalyticsWriteKeyCredential(AnalyticsWriteKey WriteKey, string Secret);

public sealed record AnalyticsExportResult(
    string FileName,
    string ContentType,
    byte[] Content,
    int ExportedRows);
