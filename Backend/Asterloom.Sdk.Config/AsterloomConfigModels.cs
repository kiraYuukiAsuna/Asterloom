using System.Collections.Concurrent;
using System.Text.Json;
using Asterloom.Targeting;

namespace Asterloom.Sdk.Config;

public enum AsterloomConfigValueKind
{
    Truth = 1,
    WholeNumber = 2,
    DecimalNumber = 3,
    Text = 4,
    Structure = 5,
}

public enum AsterloomConfigEvaluationReason
{
    TargetingMatch = 1,
    Default = 2,
}

public sealed record AsterloomConfigScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public sealed record AsterloomConfigValue
{
    public AsterloomConfigValue(
        AsterloomConfigValueKind kind,
        bool? booleanValue = null,
        long? integerValue = null,
        double? doubleValue = null,
        string? stringValue = null,
        string? jsonValue = null)
    {
        Kind = kind;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DoubleValue = doubleValue;
        StringValue = stringValue;
        JsonValue = jsonValue;
    }

    public AsterloomConfigValueKind Kind { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public double? DoubleValue { get; }

    public string? StringValue { get; }

    public string? JsonValue { get; }

    public override string ToString() =>
        $"{nameof(AsterloomConfigValue)} {{ Kind = {Kind}, Value = [REDACTED] }}";
}

public sealed record AsterloomConfigResolvedValue(
    Guid EntryId,
    string Key,
    AsterloomConfigValue Value,
    long Revision,
    AsterloomConfigEvaluationReason Reason,
    string? TargetingRuleId);

public sealed record AsterloomConfigSnapshot(
    long Version,
    string ETag,
    IReadOnlyDictionary<string, AsterloomConfigResolvedValue> Values,
    DateTimeOffset GeneratedAt,
    DateTimeOffset FetchedAt,
    bool IsLastKnownGood);

public sealed record AsterloomConfigUpdateStatus(
    bool Changed,
    long CurrentSnapshotVersion,
    string ETag,
    DateTimeOffset CheckedAt);

public sealed class AsterloomConfigSnapshotUpdatedEventArgs(
    AsterloomConfigSnapshot previous,
    AsterloomConfigSnapshot current) : EventArgs
{
    public AsterloomConfigSnapshot Previous { get; } = previous;

    public AsterloomConfigSnapshot Current { get; } = current;
}

public sealed class AsterloomConfigClientOptions
{
    public required AsterloomConfigScope Scope { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan LastKnownGoodDuration { get; init; } = TimeSpan.FromHours(24);

    public bool AllowServerValues { get; init; }

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public JsonSerializerOptions SerializerOptions { get; init; } = new(JsonSerializerDefaults.Web);

    internal void Validate(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(TimeProvider);
        ArgumentNullException.ThrowIfNull(SerializerOptions);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "HttpClient.BaseAddress must identify the Asterloom server.",
                nameof(httpClient));
        }
        if (Scope.TenantId == Guid.Empty
            || Scope.ApplicationId == Guid.Empty
            || Scope.EnvironmentId == Guid.Empty)
        {
            throw new ArgumentException("Configuration scope identifiers cannot be empty.");
        }
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentException(
                "Request timeout must be between zero and two minutes.");
        }
        if (CacheDuration < TimeSpan.Zero || CacheDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentException(
                "Cache duration must be between zero and one hour.");
        }
        if (LastKnownGoodDuration < CacheDuration
            || LastKnownGoodDuration > TimeSpan.FromDays(30))
        {
            throw new ArgumentException(
                "Last-known-good duration must include cache duration and not exceed 30 days.");
        }
    }
}

public interface IAsterloomConfigSnapshotCache
{
    ValueTask<AsterloomConfigSnapshot?> GetAsync(
        string cacheKey,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync(
        string cacheKey,
        AsterloomConfigSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryAsterloomConfigSnapshotCache : IAsterloomConfigSnapshotCache
{
    private readonly ConcurrentDictionary<string, AsterloomConfigSnapshot> _snapshots =
        new(StringComparer.Ordinal);

    public ValueTask<AsterloomConfigSnapshot?> GetAsync(
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_snapshots.GetValueOrDefault(cacheKey));
    }

    public ValueTask SetAsync(
        string cacheKey,
        AsterloomConfigSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _snapshots[cacheKey] = snapshot;
        return ValueTask.CompletedTask;
    }
}

public sealed class AsterloomConfigValueTypeException(string key, AsterloomConfigValueKind kind)
    : InvalidOperationException(
        $"Configuration '{key}' has value kind {kind}, which does not match the requested type.");

public sealed class AsterloomConfigUnavailableException(string message, Exception? innerException)
    : HttpRequestException(message, innerException);

public static class AsterloomConfigContext
{
    public static TargetingEvaluationContext Create(
        AsterloomConfigScope scope,
        string targetingKey,
        string? userId = null,
        string? clientVersion = null,
        string? platform = null,
        string? region = null,
        string? language = null,
        IReadOnlyDictionary<string, TargetingValue>? attributes = null) =>
        new(
            targetingKey,
            scope.ApplicationId,
            scope.EnvironmentId,
            userId,
            clientVersion,
            platform,
            region,
            language,
            attributes);
}
