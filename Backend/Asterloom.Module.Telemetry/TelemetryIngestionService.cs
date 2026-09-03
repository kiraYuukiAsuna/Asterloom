using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Asterloom.Modules.Telemetry;

internal sealed class TelemetryIngestionService(
    ITelemetryStore store,
    TelemetryManagementService management,
    TelemetryManagementOptions options,
    TimeProvider timeProvider)
{
    private const int MaximumRecordsPerRequest = 10_000;

    public bool IsAuthorized(string providedKey)
    {
        if (options.IngestionApiKey.Length == 0 || providedKey.Length == 0)
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.IngestionApiKey));
        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public async Task<int> IngestAsync(
        TelemetrySignalType signalType,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var parsed = TelemetryOtlpJsonParser.Parse(signalType, payload, now, MaximumRecordsPerRequest);
        var accepted = new List<TelemetryRecord>(parsed.Count);
        foreach (var group in parsed.GroupBy(static record => (record.Scope, record.ServiceName)))
        {
            if (!await store.HasActiveSourceAsync(
                    group.Key.Scope,
                    group.Key.ServiceName,
                    cancellationToken))
            {
                continue;
            }

            var settings = await management.GetSettingsAsync(
                group.Key.Scope.TenantId.ToString("D"),
                group.Key.Scope.ApplicationId.ToString("D"),
                group.Key.Scope.EnvironmentId.ToString("D"),
                cancellationToken);
            if (!IsEnabled(settings, signalType))
            {
                continue;
            }

            accepted.AddRange(signalType == TelemetrySignalType.Trace
                ? group.Where(record => IsSampled(record, settings.SamplingRatio))
                : group);
        }

        await store.AppendRecordsAsync(accepted, cancellationToken);
        return accepted.Count;
    }

    private static bool IsEnabled(TelemetrySettings settings, TelemetrySignalType signalType) =>
        signalType switch
        {
            TelemetrySignalType.Trace => settings.TracesEnabled,
            TelemetrySignalType.Metric => settings.MetricsEnabled,
            TelemetrySignalType.Log => settings.LogsEnabled,
            _ => false,
        };

    private static bool IsSampled(TelemetryRecord record, double ratio)
    {
        if (ratio >= 1)
        {
            return true;
        }

        if (ratio <= 0)
        {
            return false;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            record.TraceId.Length == 0 ? record.Id.ToString("N") : record.TraceId));
        return BitConverter.ToUInt64(hash) / (double)ulong.MaxValue < ratio;
    }
}

internal static class TelemetryIngestionEndpoints
{
    private const long MaximumPayloadBytes = 4 * 1024 * 1024;
    private const string ApiKeyHeader = "X-Asterloom-Telemetry-Key";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        MapSignal(endpoints, "traces", TelemetrySignalType.Trace);
        MapSignal(endpoints, "metrics", TelemetrySignalType.Metric);
        MapSignal(endpoints, "logs", TelemetrySignalType.Log);
    }

    private static void MapSignal(
        IEndpointRouteBuilder endpoints,
        string route,
        TelemetrySignalType signalType)
    {
        endpoints.MapPost(
                $"/api/v1/telemetry/otlp/v1/{route}",
                async Task<IResult> (
                    HttpRequest request,
                    TelemetryIngestionService ingestion,
                    CancellationToken cancellationToken) =>
                {
                    if (!ingestion.IsAuthorized(request.Headers[ApiKeyHeader].ToString()))
                    {
                        return Results.Unauthorized();
                    }

                    if (!request.HasJsonContentType())
                    {
                        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                    }

                    try
                    {
                        using var document = await JsonDocument.ParseAsync(
                            request.Body,
                            cancellationToken: cancellationToken);
                        await ingestion.IngestAsync(
                            signalType,
                            document.RootElement,
                            cancellationToken);
                        return Results.Text("{}", "application/json");
                    }
                    catch (JsonException)
                    {
                        return Results.BadRequest(new { error = "invalid_otlp_json" });
                    }
                    catch (InvalidDataException exception)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(MaximumPayloadBytes));
    }
}

internal static class TelemetryOtlpJsonParser
{
    private const int MaximumPayloadLength = 262_144;

    public static IReadOnlyList<TelemetryRecord> Parse(
        TelemetrySignalType signalType,
        JsonElement payload,
        DateTimeOffset receivedAt,
        int maximumRecords)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("invalid_otlp_payload");
        }

        var records = new List<TelemetryRecord>();
        switch (signalType)
        {
            case TelemetrySignalType.Trace:
                ParseTraces(payload, receivedAt, records, maximumRecords);
                break;
            case TelemetrySignalType.Metric:
                ParseMetrics(payload, receivedAt, records, maximumRecords);
                break;
            case TelemetrySignalType.Log:
                ParseLogs(payload, receivedAt, records, maximumRecords);
                break;
            default:
                throw new InvalidDataException("unsupported_telemetry_signal");
        }

        return records;
    }

    private static void ParseTraces(
        JsonElement payload,
        DateTimeOffset receivedAt,
        List<TelemetryRecord> output,
        int maximumRecords)
    {
        foreach (var resourceGroup in Items(payload, "resourceSpans"))
        {
            if (!TryReadContext(resourceGroup, out var context))
            {
                continue;
            }

            foreach (var scopeGroup in Items(resourceGroup, "scopeSpans"))
            {
                var scopeName = ReadNestedString(scopeGroup, "scope", "name");
                foreach (var span in Items(scopeGroup, "spans"))
                {
                    EnsureCapacity(output, maximumRecords);
                    var startedAt = ReadUnixNanoseconds(span, "startTimeUnixNano") ?? receivedAt;
                    var endedAt = ReadUnixNanoseconds(span, "endTimeUnixNano");
                    double? duration = endedAt is null
                        ? null
                        : Math.Max(0, (endedAt.Value - startedAt).TotalMilliseconds);
                    var status = Property(span, "status");
                    output.Add(CreateRecord(
                        context,
                        TelemetrySignalType.Trace,
                        startedAt,
                        ReadString(span, "traceId"),
                        ReadString(span, "spanId"),
                        ReadString(span, "name", "span"),
                        status is null ? ReadText(span, "kind") : ReadText(status.Value, "code"),
                        status is null ? string.Empty : ReadString(status.Value, "message"),
                        duration,
                        MergeAttributes(context.Attributes, scopeName, span),
                        span,
                        receivedAt));
                }
            }
        }
    }

    private static void ParseMetrics(
        JsonElement payload,
        DateTimeOffset receivedAt,
        List<TelemetryRecord> output,
        int maximumRecords)
    {
        foreach (var resourceGroup in Items(payload, "resourceMetrics"))
        {
            if (!TryReadContext(resourceGroup, out var context))
            {
                continue;
            }

            foreach (var scopeGroup in Items(resourceGroup, "scopeMetrics"))
            {
                var scopeName = ReadNestedString(scopeGroup, "scope", "name");
                foreach (var metric in Items(scopeGroup, "metrics"))
                {
                    var name = ReadString(metric, "name", "metric");
                    foreach (var metricType in new[]
                    {
                        "gauge", "sum", "histogram", "exponentialHistogram", "summary",
                    })
                    {
                        var data = Property(metric, metricType);
                        if (data is null)
                        {
                            continue;
                        }

                        foreach (var point in Items(data.Value, "dataPoints"))
                        {
                            EnsureCapacity(output, maximumRecords);
                            output.Add(CreateRecord(
                                context,
                                TelemetrySignalType.Metric,
                                ReadUnixNanoseconds(point, "timeUnixNano") ?? receivedAt,
                                string.Empty,
                                string.Empty,
                                name,
                                metricType,
                                MetricValue(point),
                                null,
                                MergeAttributes(context.Attributes, scopeName, point),
                                point,
                                receivedAt));
                        }

                        break;
                    }
                }
            }
        }
    }

    private static void ParseLogs(
        JsonElement payload,
        DateTimeOffset receivedAt,
        List<TelemetryRecord> output,
        int maximumRecords)
    {
        foreach (var resourceGroup in Items(payload, "resourceLogs"))
        {
            if (!TryReadContext(resourceGroup, out var context))
            {
                continue;
            }

            foreach (var scopeGroup in Items(resourceGroup, "scopeLogs"))
            {
                var scopeName = ReadNestedString(scopeGroup, "scope", "name");
                foreach (var log in Items(scopeGroup, "logRecords"))
                {
                    EnsureCapacity(output, maximumRecords);
                    output.Add(CreateRecord(
                        context,
                        TelemetrySignalType.Log,
                        ReadUnixNanoseconds(log, "timeUnixNano")
                            ?? ReadUnixNanoseconds(log, "observedTimeUnixNano")
                            ?? receivedAt,
                        ReadString(log, "traceId"),
                        ReadString(log, "spanId"),
                        ReadString(log, "eventName", scopeName.Length == 0 ? "log" : scopeName),
                        ReadString(log, "severityText", ReadText(log, "severityNumber")),
                        AnyValue(Property(log, "body"))?.ToString() ?? string.Empty,
                        null,
                        MergeAttributes(context.Attributes, scopeName, log),
                        log,
                        receivedAt));
                }
            }
        }
    }

    private static TelemetryRecord CreateRecord(
        OtlpContext context,
        TelemetrySignalType signalType,
        DateTimeOffset observedAt,
        string traceId,
        string spanId,
        string name,
        string category,
        string value,
        double? durationMilliseconds,
        string attributesJson,
        JsonElement payload,
        DateTimeOffset createdAt)
    {
        var rawPayloadJson = payload.GetRawText();
        var payloadJson = LimitJson(rawPayloadJson, MaximumPayloadLength);
        var identity = string.Join('|',
            context.Scope.TenantId,
            context.Scope.ApplicationId,
            context.Scope.EnvironmentId,
            (int)signalType,
            context.ServiceName,
            observedAt.ToUnixTimeMilliseconds(),
            traceId,
            spanId,
            name,
            rawPayloadJson);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new(
            new Guid(hash.AsSpan(0, 16)),
            context.Scope,
            signalType,
            Limit(context.ServiceName, 200),
            observedAt,
            NormalizeHexIdentifier(traceId, 32),
            NormalizeHexIdentifier(spanId, 16),
            Limit(name, 500),
            Limit(category, 100),
            Limit(value, 4_000),
            durationMilliseconds,
            LimitJson(attributesJson, 65_536),
            payloadJson,
            createdAt);
    }

    private static bool TryReadContext(JsonElement group, out OtlpContext context)
    {
        var resource = Property(group, "resource");
        var attributes = resource is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : ReadAttributes(resource.Value);
        if (!TryString(attributes, "service.name", out var serviceName)
            || !TryGuid(attributes, "asterloom.tenant.id", out var tenantId)
            || !TryGuid(attributes, "asterloom.application.id", out var applicationId)
            || !TryGuid(attributes, "asterloom.environment.id", out var environmentId))
        {
            context = default;
            return false;
        }

        context = new(
            new TelemetryScope(tenantId, applicationId, environmentId),
            serviceName,
            attributes);
        return true;
    }

    private static string MergeAttributes(
        IReadOnlyDictionary<string, object?> resourceAttributes,
        string scopeName,
        JsonElement record)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in resourceAttributes)
        {
            attributes[pair.Key] = pair.Value;
        }
        if (scopeName.Length > 0)
        {
            attributes["otel.scope.name"] = scopeName;
        }

        foreach (var pair in ReadAttributes(record))
        {
            attributes[pair.Key] = pair.Value;
        }

        return JsonSerializer.Serialize(attributes);
    }

    private static Dictionary<string, object?> ReadAttributes(JsonElement owner)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var attribute in Items(owner, "attributes"))
        {
            var key = ReadString(attribute, "key");
            if (key.Length > 0)
            {
                result[key] = AnyValue(Property(attribute, "value"));
            }
        }

        return result;
    }

    private static object? AnyValue(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in value.Value.EnumerateObject())
        {
            return property.Name switch
            {
                "stringValue" or "bytesValue" or "intValue" => ReadScalarString(property.Value),
                "boolValue" => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? property.Value.GetBoolean()
                    : ReadScalarString(property.Value),
                "doubleValue" => property.Value.TryGetDouble(out var number)
                    ? number
                    : ReadScalarString(property.Value),
                "arrayValue" => Items(property.Value, "values")
                    .Select(static item => AnyValue(item))
                    .ToArray(),
                "kvlistValue" => ReadKeyValues(property.Value),
                _ => property.Value.GetRawText(),
            };
        }

        return null;
    }

    private static Dictionary<string, object?> ReadKeyValues(JsonElement value)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in Items(value, "values"))
        {
            var key = ReadString(item, "key");
            if (key.Length > 0)
            {
                result[key] = AnyValue(Property(item, "value"));
            }
        }

        return result;
    }

    private static string MetricValue(JsonElement point)
    {
        foreach (var key in new[] { "asDouble", "asInt" })
        {
            var value = Property(point, key);
            if (value is not null)
            {
                return ReadScalarString(value.Value);
            }
        }

        var count = ReadText(point, "count");
        var sum = ReadText(point, "sum");
        return count.Length == 0 ? sum : sum.Length == 0 ? $"count={count}" : $"count={count} sum={sum}";
    }

    private static DateTimeOffset? ReadUnixNanoseconds(JsonElement owner, string name)
    {
        var text = ReadText(owner, name);
        if (!ulong.TryParse(text, out var nanoseconds))
        {
            return null;
        }

        var seconds = nanoseconds / 1_000_000_000;
        if (seconds > 253_402_300_799)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            .AddTicks((long)(nanoseconds % 1_000_000_000) / 100);
    }

    private static string NormalizeHexIdentifier(string value, int expectedLength)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == expectedLength && normalized.All(Uri.IsHexDigit))
        {
            return normalized;
        }

        try
        {
            var bytes = Convert.FromBase64String(normalized);
            return bytes.Length * 2 == expectedLength ? Convert.ToHexStringLower(bytes) : string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static JsonElement[] Items(JsonElement owner, string propertyName)
    {
        var property = Property(owner, propertyName);
        return property is { ValueKind: JsonValueKind.Array }
            ? property.Value.EnumerateArray().ToArray()
            : [];
    }

    private static JsonElement? Property(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var property)
            ? property
            : null;

    private static string ReadNestedString(JsonElement owner, string parent, string name) =>
        Property(owner, parent) is { } nested ? ReadString(nested, name) : string.Empty;

    private static string ReadString(JsonElement owner, string name, string fallback = "") =>
        Property(owner, name) is { } property
            ? ReadScalarString(property)
            : fallback;

    private static string ReadText(JsonElement owner, string name) =>
        Property(owner, name) is { } property ? ReadScalarString(property) : string.Empty;

    private static string ReadScalarString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => string.Empty,
    };

    private static bool TryString(
        IReadOnlyDictionary<string, object?> attributes,
        string key,
        out string value)
    {
        value = attributes.GetValueOrDefault(key)?.ToString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGuid(
        IReadOnlyDictionary<string, object?> attributes,
        string key,
        out Guid value) =>
        Guid.TryParse(attributes.GetValueOrDefault(key)?.ToString(), out value)
        && value != Guid.Empty;

    private static void EnsureCapacity(List<TelemetryRecord> records, int maximum)
    {
        if (records.Count >= maximum)
        {
            throw new InvalidDataException("too_many_telemetry_records");
        }
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static string LimitJson(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : $"{{\"truncated\":true,\"originalLength\":{value.Length}}}";

    private readonly record struct OtlpContext(
        TelemetryScope Scope,
        string ServiceName,
        IReadOnlyDictionary<string, object?> Attributes);
}
