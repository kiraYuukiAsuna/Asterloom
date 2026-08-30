namespace Asterloom.Modules.Analytics.Model;

public readonly record struct AnalyticsScope(
    Guid TenantId,
    Guid ApplicationId,
    Guid EnvironmentId);

public enum AnalyticsResourceStatus
{
    Active = 1,
    Archived = 2,
}

public enum AnalyticsWriteKeyStatus
{
    Active = 1,
    Revoked = 2,
}

public sealed record EventSchema(
    Guid Id,
    AnalyticsScope Scope,
    string Key,
    string DisplayName,
    string Description,
    string SchemaJson,
    AnalyticsResourceStatus Status,
    int RetentionDays,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AnalyticsWriteKey(
    Guid Id,
    AnalyticsScope Scope,
    string Name,
    string Prefix,
    byte[] SecretHash,
    AnalyticsWriteKeyStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record StoredAnalyticsEvent(
    Guid Id,
    string EventId,
    AnalyticsScope Scope,
    Guid EventSchemaId,
    string EventName,
    long SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string ActorId,
    string AnonymousId,
    string SessionId,
    string PropertiesJson,
    string ContextJson,
    string SdkName,
    string SdkVersion,
    string WriteKeyPrefix);

public sealed record AnalyticsPageRequest(int Offset, int PageSize, string Query, bool IncludeInactive);

public sealed record AnalyticsEventFilter(
    int Offset,
    int PageSize,
    string EventName,
    string ActorId,
    string EventId,
    DateTimeOffset? FromAt,
    DateTimeOffset? ToAt);

public sealed record AnalyticsStorePage<T>(IReadOnlyList<T> Items, bool HasMore);

public enum AnalyticsAppendOutcome
{
    Accepted = 1,
    Deduplicated = 2,
}

public sealed record AnalyticsAggregationQuery(
    AnalyticsScope Scope,
    IReadOnlyList<string> EventNames,
    DateTimeOffset FromAt,
    DateTimeOffset ToAt,
    AnalyticsInterval Interval);

public enum AnalyticsInterval
{
    Hour = 1,
    Day = 2,
    Week = 3,
}

public sealed record AnalyticsAggregationBucket(
    DateTimeOffset PeriodStart,
    string EventName,
    long EventCount,
    long UniqueActors);
