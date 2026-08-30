using Asterloom.Modules.Analytics.Model;
using Google.Protobuf.WellKnownTypes;
using ProtocolAggregationBucket = Asterloom.Protocol.Analytics.V1.AnalyticsAggregationBucket;
using ProtocolAnalyticsEvent = Asterloom.Protocol.Analytics.V1.AnalyticsEvent;
using ProtocolAnalyticsScope = Asterloom.Protocol.Analytics.V1.AnalyticsScope;
using ProtocolEventSchema = Asterloom.Protocol.Analytics.V1.EventSchema;
using ProtocolResourceStatus = Asterloom.Protocol.Analytics.V1.AnalyticsResourceStatus;
using ProtocolWriteKey = Asterloom.Protocol.Analytics.V1.AnalyticsWriteKey;
using ProtocolWriteKeyStatus = Asterloom.Protocol.Analytics.V1.AnalyticsWriteKeyStatus;

namespace Asterloom.Modules.Analytics;

internal static class AnalyticsProtocolMapper
{
    public static ProtocolAnalyticsScope ToProtocol(this AnalyticsScope scope) => new()
    {
        TenantId = scope.TenantId.ToString("D"),
        ApplicationId = scope.ApplicationId.ToString("D"),
        EnvironmentId = scope.EnvironmentId.ToString("D"),
    };

    public static ProtocolEventSchema ToProtocol(this EventSchema schema) => new()
    {
        Id = schema.Id.ToString("D"),
        Scope = schema.Scope.ToProtocol(),
        Key = schema.Key,
        DisplayName = schema.DisplayName,
        Description = schema.Description,
        SchemaJson = schema.SchemaJson,
        Status = schema.Status switch
        {
            AnalyticsResourceStatus.Active => ProtocolResourceStatus.Active,
            AnalyticsResourceStatus.Archived => ProtocolResourceStatus.Archived,
            _ => ProtocolResourceStatus.Unspecified,
        },
        RetentionDays = schema.RetentionDays,
        Version = schema.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(schema.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(schema.UpdatedAt),
        ArchivedAt = schema.ArchivedAt is { } archivedAt
            ? Timestamp.FromDateTimeOffset(archivedAt)
            : null,
    };

    public static ProtocolWriteKey ToProtocol(this AnalyticsWriteKey writeKey) => new()
    {
        Id = writeKey.Id.ToString("D"),
        Scope = writeKey.Scope.ToProtocol(),
        Name = writeKey.Name,
        Prefix = writeKey.Prefix,
        Status = writeKey.Status switch
        {
            AnalyticsWriteKeyStatus.Active => ProtocolWriteKeyStatus.Active,
            AnalyticsWriteKeyStatus.Revoked => ProtocolWriteKeyStatus.Revoked,
            _ => ProtocolWriteKeyStatus.Unspecified,
        },
        Version = writeKey.Version,
        CreatedAt = Timestamp.FromDateTimeOffset(writeKey.CreatedAt),
        UpdatedAt = Timestamp.FromDateTimeOffset(writeKey.UpdatedAt),
        LastUsedAt = writeKey.LastUsedAt is { } lastUsedAt
            ? Timestamp.FromDateTimeOffset(lastUsedAt)
            : null,
        RevokedAt = writeKey.RevokedAt is { } revokedAt
            ? Timestamp.FromDateTimeOffset(revokedAt)
            : null,
    };

    public static ProtocolAnalyticsEvent ToProtocol(this StoredAnalyticsEvent analyticsEvent) => new()
    {
        Id = analyticsEvent.Id.ToString("D"),
        EventId = analyticsEvent.EventId,
        Scope = analyticsEvent.Scope.ToProtocol(),
        EventName = analyticsEvent.EventName,
        SchemaVersion = analyticsEvent.SchemaVersion,
        OccurredAt = Timestamp.FromDateTimeOffset(analyticsEvent.OccurredAt),
        ReceivedAt = Timestamp.FromDateTimeOffset(analyticsEvent.ReceivedAt),
        ActorId = analyticsEvent.ActorId,
        AnonymousId = analyticsEvent.AnonymousId,
        SessionId = analyticsEvent.SessionId,
        PropertiesJson = analyticsEvent.PropertiesJson,
        ContextJson = analyticsEvent.ContextJson,
        SdkName = analyticsEvent.SdkName,
        SdkVersion = analyticsEvent.SdkVersion,
        WriteKeyPrefix = analyticsEvent.WriteKeyPrefix,
    };

    public static ProtocolAggregationBucket ToProtocol(this AnalyticsAggregationBucket bucket) => new()
    {
        PeriodStart = Timestamp.FromDateTimeOffset(bucket.PeriodStart),
        EventName = bucket.EventName,
        EventCount = bucket.EventCount,
        UniqueActors = bucket.UniqueActors,
    };
}
