using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asterloom.Targeting;

namespace Asterloom.Sdk.Config;

public sealed class AsterloomConfigClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AsterloomConfigClientOptions _options;
    private readonly IAsterloomConfigSnapshotCache _cache;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AsterloomConfigClient(
        HttpClient httpClient,
        AsterloomConfigClientOptions options,
        IAsterloomConfigSnapshotCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(httpClient);
        _httpClient = httpClient;
        _options = options;
        _cache = cache ?? new MemoryAsterloomConfigSnapshotCache();
        _serializerOptions = new JsonSerializerOptions(options.SerializerOptions)
        {
            NumberHandling = options.SerializerOptions.NumberHandling
                | JsonNumberHandling.AllowReadingFromString,
        };
    }

    public event EventHandler<AsterloomConfigSnapshotUpdatedEventArgs>? SnapshotUpdated;

    public async Task<AsterloomConfigSnapshot> GetSnapshotAsync(
        TargetingEvaluationContext context,
        bool includeServerValues = false,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        if (includeServerValues && !_options.AllowServerValues)
        {
            throw new InvalidOperationException(
                "Server-visible configuration access must be enabled explicitly in client options.");
        }

        var cacheKey = CreateCacheKey(context, includeServerValues);
        var now = _options.TimeProvider.GetUtcNow();
        var cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (!forceRefresh
            && cached is not null
            && now - cached.FetchedAt <= _options.CacheDuration)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _options.TimeProvider.GetUtcNow();
            cached = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (!forceRefresh
                && cached is not null
                && now - cached.FetchedAt <= _options.CacheDuration)
            {
                return cached;
            }

            try
            {
                return await FetchSnapshotAsync(
                    context,
                    includeServerValues,
                    cacheKey,
                    cached,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or JsonException
                    or TaskCanceledException)
            {
                if (cached is not null
                    && now - cached.FetchedAt <= _options.LastKnownGoodDuration)
                {
                    return cached with { IsLastKnownGood = true };
                }
                throw new AsterloomConfigUnavailableException(
                    "The configuration snapshot is unavailable and no valid last-known-good snapshot exists.",
                    exception);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<AsterloomConfigUpdateStatus> CheckForUpdatesAsync(
        TargetingEvaluationContext context,
        long knownSnapshotVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        ArgumentOutOfRangeException.ThrowIfNegative(knownSnapshotVersion);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreatePath("config:check-updates"))
        {
            Content = JsonContent.Create(
                new UpdateCheckRequestDto(
                    knownSnapshotVersion,
                    ToDto(context)),
                options: _serializerOptions),
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _httpClient.SendAsync(request, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UpdateStatusDto>(
            _serializerOptions,
            timeout.Token).ConfigureAwait(false)
            ?? throw new JsonException("The configuration update response is empty.");
        return new(
            payload.Changed,
            payload.CurrentSnapshotVersion,
            payload.Etag,
            payload.CheckedAt);
    }

    public async Task<bool> GetBooleanAsync(
        string key,
        bool defaultValue,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, context, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return defaultValue;
        }
        return value.Kind == AsterloomConfigValueKind.Truth
            ? value.BooleanValue!.Value
            : throw new AsterloomConfigValueTypeException(key, value.Kind);
    }

    public async Task<long> GetInt64Async(
        string key,
        long defaultValue,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, context, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return defaultValue;
        }
        return value.Kind == AsterloomConfigValueKind.WholeNumber
            ? value.IntegerValue!.Value
            : throw new AsterloomConfigValueTypeException(key, value.Kind);
    }

    public async Task<double> GetDoubleAsync(
        string key,
        double defaultValue,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, context, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return defaultValue;
        }
        return value.Kind == AsterloomConfigValueKind.DecimalNumber
            ? value.DoubleValue!.Value
            : throw new AsterloomConfigValueTypeException(key, value.Kind);
    }

    public async Task<string> GetStringAsync(
        string key,
        string defaultValue,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        var value = await GetValueAsync(key, context, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return defaultValue;
        }
        return value.Kind == AsterloomConfigValueKind.Text
            ? value.StringValue!
            : throw new AsterloomConfigValueTypeException(key, value.Kind);
    }

    public async Task<T?> GetJsonAsync<T>(
        string key,
        T? defaultValue,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, context, cancellationToken).ConfigureAwait(false);
        if (value is null)
        {
            return defaultValue;
        }
        if (value.Kind != AsterloomConfigValueKind.Structure)
        {
            throw new AsterloomConfigValueTypeException(key, value.Kind);
        }
        return JsonSerializer.Deserialize<T>(value.JsonValue!, _serializerOptions);
    }

    private async Task<AsterloomConfigValue?> GetValueAsync(
        string key,
        TargetingEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration key is required.", nameof(key));
        }
        var snapshot = await GetSnapshotAsync(
            context,
            includeServerValues: false,
            forceRefresh: false,
            cancellationToken).ConfigureAwait(false);
        return snapshot.Values.GetValueOrDefault(key)?.Value;
    }

    private async Task<AsterloomConfigSnapshot> FetchSnapshotAsync(
        TargetingEvaluationContext context,
        bool includeServerValues,
        string cacheKey,
        AsterloomConfigSnapshot? cached,
        CancellationToken cancellationToken)
    {
        var operation = includeServerValues ? "config:server-snapshot" : "config:snapshot";
        using var request = new HttpRequestMessage(HttpMethod.Post, CreatePath(operation))
        {
            Content = JsonContent.Create(
                new SnapshotRequestDto(ToDto(context), cached?.ETag ?? string.Empty),
                options: _serializerOptions),
        };
        if (!string.IsNullOrWhiteSpace(cached?.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        using var response = await _httpClient.SendAsync(request, timeout.Token)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            return await StoreNotModifiedAsync(cacheKey, cached, cancellationToken)
                .ConfigureAwait(false);
        }
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SnapshotResponseDto>(
            _serializerOptions,
            timeout.Token).ConfigureAwait(false)
            ?? throw new JsonException("The configuration snapshot response is empty.");
        if (payload.NotModified)
        {
            if (cached is null)
            {
                throw new JsonException(
                    "The server returned not-modified without a cached configuration snapshot.");
            }
            return await StoreNotModifiedAsync(cacheKey, cached, cancellationToken)
                .ConfigureAwait(false);
        }

        var values = (payload.Values ?? [])
            .Select(ToModel)
            .ToDictionary(static item => item.Key, StringComparer.Ordinal);
        var current = new AsterloomConfigSnapshot(
            payload.SnapshotVersion,
            payload.Etag,
            values,
            payload.GeneratedAt,
            _options.TimeProvider.GetUtcNow(),
            IsLastKnownGood: false);
        await _cache.SetAsync(cacheKey, current, cancellationToken).ConfigureAwait(false);
        if (cached is not null && cached.Version != current.Version)
        {
            SnapshotUpdated?.Invoke(
                this,
                new AsterloomConfigSnapshotUpdatedEventArgs(cached, current));
        }
        return current;
    }

    private async Task<AsterloomConfigSnapshot> StoreNotModifiedAsync(
        string cacheKey,
        AsterloomConfigSnapshot cached,
        CancellationToken cancellationToken)
    {
        var refreshed = cached with
        {
            FetchedAt = _options.TimeProvider.GetUtcNow(),
            IsLastKnownGood = false,
        };
        await _cache.SetAsync(cacheKey, refreshed, cancellationToken).ConfigureAwait(false);
        return refreshed;
    }

    private string CreatePath(string operation)
    {
        var scope = _options.Scope;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"api/v1/tenants/{scope.TenantId:D}/applications/{scope.ApplicationId:D}/environments/{scope.EnvironmentId:D}/{operation}");
    }

    private void ValidateContext(TargetingEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TargetingContract.ValidateContext(context);
        if (context.ApplicationId != _options.Scope.ApplicationId
            || context.EnvironmentId != _options.Scope.EnvironmentId)
        {
            throw new ArgumentException(
                "Evaluation context scope must match the configuration client scope.",
                nameof(context));
        }
    }

    private string CreateCacheKey(
        TargetingEvaluationContext context,
        bool includeServerValues)
    {
        var serialized = JsonSerializer.Serialize(
            new
            {
                scope = _options.Scope,
                visibility = includeServerValues ? "server" : "client",
                context = ToDto(context),
            },
            _serializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)))
            .ToLowerInvariant();
    }

    private static ContextDto ToDto(TargetingEvaluationContext context) =>
        new(
            context.TargetingKey,
            context.UserId ?? string.Empty,
            context.ClientVersion ?? string.Empty,
            context.Platform ?? string.Empty,
            context.Region ?? string.Empty,
            context.Language ?? string.Empty,
            context.Attributes
                .OrderBy(static item => item.Key, StringComparer.Ordinal)
                .Select(item => new AttributeDto(item.Key, ToDto(item.Value)))
                .ToArray());

    private static TargetingValueDto ToDto(TargetingValue value) => value.Kind switch
    {
        TargetingValueKind.Text => new(Text: value.StringValue),
        TargetingValueKind.Truth => new(Truth: value.BooleanValue),
        TargetingValueKind.Numeric => new(Numeric: value.NumberValue),
        _ => throw new ArgumentException("A targeting attribute has an invalid value kind."),
    };

    private static AsterloomConfigResolvedValue ToModel(EffectiveValueDto value)
    {
        var kind = value.ValueKind switch
        {
            "CONFIG_VALUE_KIND_BOOLEAN" => AsterloomConfigValueKind.Truth,
            "CONFIG_VALUE_KIND_INTEGER" => AsterloomConfigValueKind.WholeNumber,
            "CONFIG_VALUE_KIND_DOUBLE" => AsterloomConfigValueKind.DecimalNumber,
            "CONFIG_VALUE_KIND_STRING" => AsterloomConfigValueKind.Text,
            "CONFIG_VALUE_KIND_JSON" => AsterloomConfigValueKind.Structure,
            _ => throw new JsonException($"Unsupported configuration value kind '{value.ValueKind}'."),
        };
        var modelValue = kind switch
        {
            AsterloomConfigValueKind.Truth =>
                new AsterloomConfigValue(kind, booleanValue: value.Value.BooleanValue),
            AsterloomConfigValueKind.WholeNumber =>
                new AsterloomConfigValue(kind, integerValue: value.Value.IntegerValue),
            AsterloomConfigValueKind.DecimalNumber =>
                new AsterloomConfigValue(kind, doubleValue: value.Value.DoubleValue),
            AsterloomConfigValueKind.Text =>
                new AsterloomConfigValue(kind, stringValue: value.Value.StringValue),
            AsterloomConfigValueKind.Structure =>
                new AsterloomConfigValue(kind, jsonValue: value.Value.JsonValue),
            _ => throw new JsonException("The configuration value is invalid."),
        };
        var reason = value.Reason switch
        {
            "CONFIG_EVALUATION_REASON_TARGETING_MATCH" =>
                AsterloomConfigEvaluationReason.TargetingMatch,
            "CONFIG_EVALUATION_REASON_DEFAULT" => AsterloomConfigEvaluationReason.Default,
            _ => throw new JsonException(
                $"Unsupported configuration evaluation reason '{value.Reason}'."),
        };
        if (!Guid.TryParse(value.EntryId, out var entryId))
        {
            throw new JsonException("A configuration entry identifier is invalid.");
        }
        return new(
            entryId,
            value.Key,
            modelValue,
            value.Revision,
            reason,
            string.IsNullOrWhiteSpace(value.TargetingRuleId) ? null : value.TargetingRuleId);
    }

    private sealed record SnapshotRequestDto(ContextDto Context, string IfNoneMatch);

    private sealed record UpdateCheckRequestDto(long KnownSnapshotVersion, ContextDto Context);

    private sealed record ContextDto(
        string TargetingKey,
        string UserId,
        string ClientVersion,
        string Platform,
        string Region,
        string Language,
        IReadOnlyList<AttributeDto> Attributes);

    private sealed record AttributeDto(string Key, TargetingValueDto Value);

    private sealed record TargetingValueDto(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Truth = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Numeric = null);

    private sealed record SnapshotResponseDto(
        long SnapshotVersion,
        string Etag,
        bool NotModified,
        IReadOnlyList<EffectiveValueDto>? Values,
        DateTimeOffset GeneratedAt);

    private sealed record EffectiveValueDto(
        string EntryId,
        string Key,
        string ValueKind,
        ValueDto Value,
        long Revision,
        string Reason,
        string TargetingRuleId);

    private sealed record ValueDto(
        bool? BooleanValue,
        long? IntegerValue,
        double? DoubleValue,
        string? StringValue,
        string? JsonValue);

    private sealed record UpdateStatusDto(
        bool Changed,
        long CurrentSnapshotVersion,
        string Etag,
        DateTimeOffset CheckedAt);

    public void Dispose()
    {
        _refreshLock.Dispose();
    }
}
