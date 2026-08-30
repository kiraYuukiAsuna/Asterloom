using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asterloom.Protocol.Feature.V1;
using Google.Protobuf;
using Grpc.Core;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using OpenFeatureMetadata = OpenFeature.Model.Metadata;
using ProtocolContext = Asterloom.Protocol.Targeting.V1.EvaluationContext;
using ProtocolTargetingValue = Asterloom.Protocol.Targeting.V1.TargetingValue;
using ProtocolValue = Asterloom.Protocol.Feature.V1.FeatureValue;
using ProtocolValueKind = Asterloom.Protocol.Feature.V1.FeatureValueKind;

namespace Asterloom.Sdk.Feature;

public sealed class AsterloomFeatureProvider : FeatureProvider
{
    private static readonly OpenFeatureMetadata ProviderMetadata = new("Asterloom");
    private static readonly HashSet<string> ReservedContextKeys = new(
        [
            "targetingKey",
            "userId",
            "applicationId",
            "environmentId",
            "clientVersion",
            "platform",
            "region",
            "language",
        ],
        StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly FeatureService.FeatureServiceClient _client;
    private readonly AsterloomFeatureProviderOptions _options;

    public AsterloomFeatureProvider(
        CallInvoker callInvoker,
        AsterloomFeatureProviderOptions options)
        : this(
            new FeatureService.FeatureServiceClient(
                callInvoker ?? throw new ArgumentNullException(nameof(callInvoker))),
            options)
    {
    }

    public AsterloomFeatureProvider(
        FeatureService.FeatureServiceClient client,
        AsterloomFeatureProviderOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public override OpenFeatureMetadata GetMetadata() => ProviderMetadata;

    public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            flagKey,
            defaultValue,
            ProtocolValueKind.Boolean,
            context,
            static value => value.ValueCase == ProtocolValue.ValueOneofCase.BooleanValue
                ? value.BooleanValue
                : throw new FeatureTypeException(),
            cancellationToken);

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey,
        string defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            flagKey,
            defaultValue,
            ProtocolValueKind.String,
            context,
            static value => value.ValueCase == ProtocolValue.ValueOneofCase.StringValue
                ? value.StringValue
                : throw new FeatureTypeException(),
            cancellationToken);

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey,
        int defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            flagKey,
            defaultValue,
            ProtocolValueKind.Integer,
            context,
            static value => value.ValueCase == ProtocolValue.ValueOneofCase.IntegerValue
                && value.IntegerValue is >= int.MinValue and <= int.MaxValue
                    ? (int)value.IntegerValue
                    : throw new FeatureTypeException(),
            cancellationToken);

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey,
        double defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            flagKey,
            defaultValue,
            ProtocolValueKind.Double,
            context,
            static value => value.ValueCase == ProtocolValue.ValueOneofCase.DoubleValue
                && double.IsFinite(value.DoubleValue)
                    ? value.DoubleValue
                    : throw new FeatureTypeException(),
            cancellationToken);

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey,
        Value defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            flagKey,
            defaultValue,
            ProtocolValueKind.Object,
            context,
            static value => value.ValueCase == ProtocolValue.ValueOneofCase.ObjectJson
                ? ParseStructure(value.ObjectJson)
                : throw new FeatureTypeException(),
            cancellationToken);

    public void ClearCache() => _cache.Clear();

    private async Task<ResolutionDetails<T>> ResolveAsync<T>(
        string flagKey,
        T defaultValue,
        ProtocolValueKind expectedKind,
        EvaluationContext? context,
        Func<ProtocolValue, T> convert,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(flagKey) || flagKey.Length > 100)
        {
            return Error(
                flagKey ?? string.Empty,
                defaultValue,
                ErrorType.FlagNotFound,
                "A stable flag key is required.");
        }

        ProtocolContext protocolContext;
        try
        {
            protocolContext = ToProtocolContext(context ?? EvaluationContext.Empty);
        }
        catch (InvalidFeatureContextException exception)
        {
            return Error(
                flagKey,
                defaultValue,
                exception.TargetingKeyMissing
                    ? ErrorType.TargetingKeyMissing
                    : ErrorType.InvalidContext,
                exception.Message);
        }

        var cacheKey = CreateCacheKey(flagKey, expectedKind, protocolContext);
        var now = _options.TimeProvider.GetUtcNow();
        if (_cache.TryGetValue(cacheKey, out var cached)
            && now - cached.StoredAt <= _options.CacheDuration)
        {
            try
            {
                return Success(flagKey, convert(cached.Details.Value), cached.Details, Reason.Cached, true);
            }
            catch (FeatureTypeException)
            {
                _cache.TryRemove(cacheKey, out _);
            }
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);
            var response = await _client.EvaluateFlagAsync(
                new FeatureEvaluationInput
                {
                    TenantId = _options.Scope.TenantId.ToString("D"),
                    ApplicationId = _options.Scope.ApplicationId.ToString("D"),
                    EnvironmentId = _options.Scope.EnvironmentId.ToString("D"),
                    FlagKey = flagKey.Trim(),
                    ExpectedKind = expectedKind,
                    Context = protocolContext,
                },
                cancellationToken: timeout.Token);
            var value = convert(response.Value);
            _cache[cacheKey] = new CacheEntry(response.Clone(), now);
            return Success(flagKey, value, response, MapReason(response.Reason), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is RpcException
            or OperationCanceledException
            or FeatureTypeException)
        {
            if (_cache.TryGetValue(cacheKey, out var lastKnownGood)
                && now - lastKnownGood.StoredAt <= _options.LastKnownGoodDuration)
            {
                try
                {
                    return Success(
                        flagKey,
                        convert(lastKnownGood.Details.Value),
                        lastKnownGood.Details,
                        Reason.Cached,
                        true);
                }
                catch (FeatureTypeException)
                {
                    _cache.TryRemove(cacheKey, out _);
                }
            }

            return Error(
                flagKey,
                defaultValue,
                MapError(exception),
                SafeErrorMessage(exception));
        }
    }

    private static ProtocolContext ToProtocolContext(EvaluationContext context)
    {
        var targetingKey = context.TargetingKey;
        if (string.IsNullOrWhiteSpace(targetingKey))
        {
            throw new InvalidFeatureContextException(
                "OpenFeature evaluation context requires a targeting key.",
                targetingKeyMissing: true);
        }

        var result = new ProtocolContext
        {
            TargetingKey = targetingKey,
            UserId = GetOptionalString(context, "userId"),
            ClientVersion = GetOptionalString(context, "clientVersion"),
            Platform = GetOptionalString(context, "platform"),
            Region = GetOptionalString(context, "region"),
            Language = GetOptionalString(context, "language"),
        };
        foreach (var (key, value) in context.AsDictionary()
                     .OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            if (ReservedContextKeys.Contains(key))
            {
                continue;
            }

            var protocolValue = value.IsString
                ? new ProtocolTargetingValue { Text = value.AsString }
                : value.IsBoolean
                    ? new ProtocolTargetingValue { Truth = value.AsBoolean!.Value }
                    : value.IsNumber
                        ? new ProtocolTargetingValue { Numeric = value.AsDouble!.Value }
                        : throw new InvalidFeatureContextException(
                            $"Targeting attribute '{key}' must be a string, boolean, or number.");
            result.Attributes.Add(new Asterloom.Protocol.Targeting.V1.TargetingAttribute
            {
                Key = key,
                Value = protocolValue,
            });
        }

        return result;
    }

    private static string GetOptionalString(EvaluationContext context, string key)
    {
        if (!context.TryGetValue(key, out var value) || value is null || value.IsNull)
        {
            return string.Empty;
        }

        if (!value.IsString || string.IsNullOrWhiteSpace(value.AsString))
        {
            throw new InvalidFeatureContextException($"Context field '{key}' must be a string.");
        }

        return value.AsString;
    }

    private static string CreateCacheKey(
        string flagKey,
        ProtocolValueKind expectedKind,
        ProtocolContext context)
    {
        var material = Encoding.UTF8.GetBytes(
            $"{flagKey.Trim()}\0{(int)expectedKind}\0{Convert.ToBase64String(context.ToByteArray())}");
        return Convert.ToHexString(SHA256.HashData(material));
    }

    private static ResolutionDetails<T> Success<T>(
        string flagKey,
        T value,
        FeatureEvaluationDetails details,
        string reason,
        bool cached) =>
        new(
            flagKey,
            value,
            ErrorType.None,
            reason,
            details.VariantKey,
            string.Empty,
            new ImmutableMetadata(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["asterloom.revision"] = details.Revision,
                ["asterloom.bucketing_version"] = details.BucketingVersion,
                ["asterloom.cached"] = cached,
            }));

    private static ResolutionDetails<T> Error<T>(
        string flagKey,
        T defaultValue,
        ErrorType errorType,
        string errorMessage) =>
        new(
            flagKey,
            defaultValue,
            errorType,
            Reason.Error,
            string.Empty,
            errorMessage,
            new ImmutableMetadata());

    private static string MapReason(FeatureEvaluationReason reason) => reason switch
    {
        FeatureEvaluationReason.Disabled => Reason.Disabled,
        FeatureEvaluationReason.TargetingMatch => Reason.TargetingMatch,
        FeatureEvaluationReason.Split => Reason.Split,
        FeatureEvaluationReason.Default => Reason.Default,
        FeatureEvaluationReason.PrerequisiteFailed => Reason.Default,
        _ => Reason.Unknown,
    };

    private static ErrorType MapError(Exception exception)
    {
        if (exception is FeatureTypeException)
        {
            return ErrorType.TypeMismatch;
        }

        if (exception is OperationCanceledException)
        {
            return ErrorType.General;
        }

        if (exception is not RpcException rpc)
        {
            return ErrorType.General;
        }

        var errorCode = rpc.Trailers.GetValue("x-asterloom-error-code");
        if (string.Equals(errorCode, "feature_type_mismatch", StringComparison.Ordinal))
        {
            return ErrorType.TypeMismatch;
        }

        return rpc.StatusCode switch
        {
            StatusCode.NotFound => ErrorType.FlagNotFound,
            StatusCode.Unavailable => ErrorType.ProviderNotReady,
            StatusCode.InvalidArgument => ErrorType.InvalidContext,
            _ => ErrorType.General,
        };
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        FeatureTypeException => "The resolved feature value has the wrong type.",
        OperationCanceledException => "The Asterloom feature evaluation timed out.",
        RpcException rpc when rpc.StatusCode == StatusCode.NotFound =>
            "The feature flag was not found.",
        RpcException rpc when rpc.StatusCode == StatusCode.Unavailable =>
            "The Asterloom feature service is unavailable.",
        _ => "The Asterloom feature evaluation failed.",
    };

    private static Value ParseStructure(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FeatureTypeException();
            }

            return ToOpenFeatureValue(document.RootElement);
        }
        catch (JsonException)
        {
            throw new FeatureTypeException();
        }
    }

    private static Value ToOpenFeatureValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new Value(new Structure(element.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => ToOpenFeatureValue(property.Value),
            StringComparer.Ordinal))),
        JsonValueKind.Array => new Value(
            element.EnumerateArray().Select(ToOpenFeatureValue).ToList()),
        JsonValueKind.String => new Value(element.GetString()!),
        JsonValueKind.Number => new Value(element.GetDouble()),
        JsonValueKind.True => new Value(true),
        JsonValueKind.False => new Value(false),
        JsonValueKind.Null => new Value(),
        _ => throw new FeatureTypeException(),
    };

    private sealed record CacheEntry(FeatureEvaluationDetails Details, DateTimeOffset StoredAt);

    private sealed class FeatureTypeException : Exception;

    private sealed class InvalidFeatureContextException(
        string message,
        bool targetingKeyMissing = false) : Exception(message)
    {
        public bool TargetingKeyMissing { get; } = targetingKeyMissing;
    }
}
