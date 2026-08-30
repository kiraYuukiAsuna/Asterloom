using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;
using Microsoft.AspNetCore.WebUtilities;

namespace Asterloom.Modules.Telemetry;

public sealed partial class TelemetryManagementService(
    ITelemetryStore store,
    TelemetryCollectorHealthProbe collectorHealthProbe,
    TelemetryManagementOptions options,
    TimeProvider timeProvider)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int MaximumAttributes = 32;

    public async Task<TelemetryListResult<TelemetrySource>> ListSourcesAsync(
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
        var page = await store.ListSourcesAsync(
            ParseScope(tenantId, applicationId, environmentId),
            request,
            cancellationToken);
        return new(
            page.Items,
            page.HasMore ? EncodeOffset(request.Offset + page.Items.Count) : string.Empty);
    }

    public Task<TelemetrySource> GetSourceAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string sourceId,
        CancellationToken cancellationToken) => RequireSourceAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(sourceId, "sourceId"),
            cancellationToken);

    public async Task<TelemetrySource> CreateSourceAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string key,
        string displayName,
        string description,
        string serviceName,
        string resourceAttributesJson,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var source = new TelemetrySource(
            Guid.CreateVersion7(now),
            ParseScope(tenantId, applicationId, environmentId),
            NormalizeKey(key),
            RequireText(displayName, "displayName", 200),
            NormalizeText(description, "description", 2_000),
            RequireText(serviceName, "serviceName", 200),
            NormalizeResourceAttributes(resourceAttributesJson),
            TelemetryResourceStatus.Active,
            1,
            now,
            now,
            null);
        if (!await store.TryCreateSourceAsync(source, cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.AlreadyExists,
                "telemetry_source_exists",
                "A telemetry source with the same key or service name already exists in this environment.");
        }

        return source;
    }

    public async Task<TelemetrySource> UpdateSourceAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string sourceId,
        string displayName,
        string description,
        string serviceName,
        string resourceAttributesJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        var current = await RequireSourceAsync(
            scope,
            ParseId(sourceId, "sourceId"),
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        EnsureActive(current);
        var updated = current with
        {
            DisplayName = RequireText(displayName, "displayName", 200),
            Description = NormalizeText(description, "description", 2_000),
            ServiceName = RequireText(serviceName, "serviceName", 200),
            ResourceAttributesJson = NormalizeResourceAttributes(resourceAttributesJson),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        await SaveSourceAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    public Task<TelemetrySource> ArchiveSourceAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string sourceId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeSourceStatusAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(sourceId, "sourceId"),
            expectedVersion,
            TelemetryResourceStatus.Archived,
            cancellationToken);

    public Task<TelemetrySource> RestoreSourceAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string sourceId,
        long expectedVersion,
        CancellationToken cancellationToken) => ChangeSourceStatusAsync(
            ParseScope(tenantId, applicationId, environmentId),
            ParseId(sourceId, "sourceId"),
            expectedVersion,
            TelemetryResourceStatus.Active,
            cancellationToken);

    public async Task<TelemetrySettings> GetSettingsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var scope = ParseScope(tenantId, applicationId, environmentId);
        return await store.GetSettingsAsync(scope, cancellationToken)
            ?? new(
                scope,
                1,
                TracesEnabled: true,
                MetricsEnabled: true,
                LogsEnabled: true,
                ValidateUrl(options.DefaultExporterEndpoint, "exporterEndpoint", allowEmpty: false),
                TelemetryOtlpProtocol.Grpc,
                ValidateUrl(options.DefaultDiagnosticsBaseUrl, "diagnosticsBaseUrl", allowEmpty: true),
                Version: 0,
                DateTimeOffset.UnixEpoch);
    }

    public async Task<TelemetrySettings> UpdateSettingsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        double samplingRatio,
        bool tracesEnabled,
        bool metricsEnabled,
        bool logsEnabled,
        string exporterEndpoint,
        TelemetryOtlpProtocol exporterProtocol,
        string diagnosticsBaseUrl,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await GetSettingsAsync(
            tenantId,
            applicationId,
            environmentId,
            cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        if (!double.IsFinite(samplingRatio) || samplingRatio is < 0 or > 1)
        {
            throw Invalid("samplingRatio", "Sampling ratio must be between 0 and 1.");
        }

        if (exporterProtocol is not (TelemetryOtlpProtocol.Grpc or TelemetryOtlpProtocol.HttpProtobuf))
        {
            throw Invalid("exporterProtocol", "A supported OTLP protocol is required.");
        }

        var updated = current with
        {
            SamplingRatio = samplingRatio,
            TracesEnabled = tracesEnabled,
            MetricsEnabled = metricsEnabled,
            LogsEnabled = logsEnabled,
            ExporterEndpoint = ValidateUrl(exporterEndpoint, "exporterEndpoint", allowEmpty: false),
            ExporterProtocol = exporterProtocol,
            DiagnosticsBaseUrl = ValidateUrl(diagnosticsBaseUrl, "diagnosticsBaseUrl", allowEmpty: true),
            Version = current.Version + 1,
            UpdatedAt = timeProvider.GetUtcNow(),
        };
        if (!await store.TryUpsertSettingsAsync(updated, current.Version, cancellationToken))
        {
            throw Conflict();
        }

        return updated;
    }

    public Task<TelemetryCollectorHealth> GetCollectorHealthAsync(
        CancellationToken cancellationToken) => collectorHealthProbe.CheckAsync(cancellationToken);

    public async Task<TelemetryListResult<TelemetryError>> ListRecentErrorsAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        int pageSize,
        string? pageToken,
        string? serviceName,
        string? traceId,
        CancellationToken cancellationToken)
    {
        var pageRequest = CreatePageRequest(pageSize, pageToken, null, includeArchived: false);
        var filter = new TelemetryErrorFilter(
            pageRequest.Offset,
            pageRequest.PageSize,
            NormalizeText(serviceName, "serviceName", 200),
            NormalizeTraceId(traceId, required: false));
        var page = await store.ListErrorsAsync(
            ParseScope(tenantId, applicationId, environmentId),
            filter,
            cancellationToken);
        return new(
            page.Items,
            page.HasMore ? EncodeOffset(filter.Offset + page.Items.Count) : string.Empty);
    }

    public async Task<TelemetryDiagnosticLink> GetDiagnosticLinkAsync(
        string tenantId,
        string applicationId,
        string environmentId,
        string traceId,
        DateTimeOffset? fromAt,
        DateTimeOffset? toAt,
        CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(
            tenantId,
            applicationId,
            environmentId,
            cancellationToken);
        if (settings.DiagnosticsBaseUrl.Length == 0)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "telemetry_diagnostics_not_configured",
                "Configure a diagnostics base URL before requesting a diagnostic link.");
        }

        var normalizedTraceId = NormalizeTraceId(traceId, required: true);
        var now = timeProvider.GetUtcNow();
        var effectiveFrom = fromAt ?? now.AddMinutes(-15);
        var effectiveTo = toAt ?? now.AddMinutes(15);
        if (effectiveFrom >= effectiveTo || effectiveTo - effectiveFrom > TimeSpan.FromDays(7))
        {
            throw Invalid("timeRange", "The diagnostic range must be positive and no longer than seven days.");
        }

        var url = QueryHelpers.AddQueryString(
            settings.DiagnosticsBaseUrl,
            new Dictionary<string, string?>
            {
                ["traceId"] = normalizedTraceId,
                ["from"] = effectiveFrom.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["to"] = effectiveTo.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            });
        return new(url, normalizedTraceId, effectiveFrom, effectiveTo);
    }

    private async Task<TelemetrySource> ChangeSourceStatusAsync(
        TelemetryScope scope,
        Guid sourceId,
        long expectedVersion,
        TelemetryResourceStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var current = await RequireSourceAsync(scope, sourceId, cancellationToken);
        EnsureVersion(expectedVersion, current.Version);
        if (current.Status == targetStatus)
        {
            return current;
        }

        var now = timeProvider.GetUtcNow();
        var updated = current with
        {
            Status = targetStatus,
            Version = current.Version + 1,
            UpdatedAt = now,
            ArchivedAt = targetStatus == TelemetryResourceStatus.Archived ? now : null,
        };
        await SaveSourceAsync(updated, current.Version, cancellationToken);
        return updated;
    }

    private async Task<TelemetrySource> RequireSourceAsync(
        TelemetryScope scope,
        Guid sourceId,
        CancellationToken cancellationToken) =>
        await store.GetSourceAsync(scope, sourceId, cancellationToken)
        ?? throw new AsterloomException(
            AsterloomErrorKind.NotFound,
            "telemetry_source_not_found",
            "The telemetry source was not found.");

    private async Task SaveSourceAsync(
        TelemetrySource source,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryUpdateSourceAsync(source, expectedVersion, cancellationToken))
        {
            throw Conflict();
        }
    }

    private static void EnsureActive(TelemetrySource source)
    {
        if (source.Status != TelemetryResourceStatus.Active)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "telemetry_source_archived",
                "An archived telemetry source cannot be updated.");
        }
    }

    private static void EnsureVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw Conflict();
        }
    }

    private static TelemetryPageRequest CreatePageRequest(
        int pageSize,
        string? pageToken,
        string? query,
        bool includeArchived) => new(
            DecodeOffset(pageToken),
            pageSize == 0 ? DefaultPageSize : Math.Clamp(pageSize, 1, MaximumPageSize),
            NormalizeText(query, "query", 200),
            includeArchived);

    private static TelemetryScope ParseScope(
        string tenantId,
        string applicationId,
        string environmentId) => new(
            ParseId(tenantId, "tenantId"),
            ParseId(applicationId, "applicationId"),
            ParseId(environmentId, "environmentId"));

    private static Guid ParseId(string value, string field) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw Invalid(field, "A valid identifier is required.");

    private static string NormalizeKey(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return KeyPattern().IsMatch(normalized)
            ? normalized
            : throw Invalid("key", "Use a lowercase key containing letters, numbers, dots, dashes, or underscores.");
    }

    private static string RequireText(string? value, string field, int maximumLength)
    {
        var normalized = NormalizeText(value, field, maximumLength);
        return normalized.Length > 0
            ? normalized
            : throw Invalid(field, "A value is required.");
    }

    private static string NormalizeText(string? value, string field, int maximumLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw Invalid(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private static string NormalizeResourceAttributes(string? value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value;
        if (json.Length > 8_192)
        {
            throw Invalid("resourceAttributesJson", "Resource attributes cannot exceed 8192 characters.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("resourceAttributesJson", "Resource attributes must be a JSON object.");
            }

            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length > MaximumAttributes)
            {
                throw Invalid("resourceAttributesJson", $"At most {MaximumAttributes} resource attributes are allowed.");
            }

            foreach (var property in properties)
            {
                if (!AttributeKeyPattern().IsMatch(property.Name)
                    || property.Name is "service.name" or "deployment.environment.name"
                    || property.Name.StartsWith("asterloom.", StringComparison.Ordinal))
                {
                    throw Invalid("resourceAttributesJson", $"Resource attribute '{property.Name}' is reserved or invalid.");
                }

                if (property.Value.ValueKind is not (
                    JsonValueKind.String or JsonValueKind.Number
                    or JsonValueKind.True or JsonValueKind.False))
                {
                    throw Invalid("resourceAttributesJson", "Resource attribute values must be strings, numbers, or booleans.");
                }
            }

            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw Invalid("resourceAttributesJson", "Resource attributes must contain valid JSON.");
        }
    }

    private static string ValidateUrl(string? value, string field, bool allowEmpty)
    {
        var normalized = NormalizeText(value, field, 2_048);
        if (normalized.Length == 0 && allowEmpty)
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Invalid(field, "An absolute HTTP(S) URL without embedded credentials is required.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private static string NormalizeTraceId(string? value, bool required)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 && !required)
        {
            return string.Empty;
        }

        return TraceIdPattern().IsMatch(normalized)
            ? normalized
            : throw Invalid("traceId", "A 32-character W3C trace ID is required.");
    }

    private static int DecodeOffset(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            return int.TryParse(decoded, CultureInfo.InvariantCulture, out var offset) && offset >= 0
                ? offset
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw Invalid("pageToken", "The page token is invalid.");
        }
    }

    private static string EncodeOffset(int offset) => WebEncoders.Base64UrlEncode(
        System.Text.Encoding.UTF8.GetBytes(offset.ToString(CultureInfo.InvariantCulture)));

    private static AsterloomException Invalid(string field, string description) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [description],
        });

    private static AsterloomException Conflict() => new(
        AsterloomErrorKind.Conflict,
        "concurrency_conflict",
        "The resource changed after it was loaded. Refresh and retry.");

    [GeneratedRegex("^[a-z][a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeKeyPattern();

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceIdPattern();
}
