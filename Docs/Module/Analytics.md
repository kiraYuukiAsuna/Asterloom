# Analytics: Product Events, Schemas, and Aggregation

[English](Analytics.md) | [简体中文](Analytics.zh-CN.md) | [Module index](README.md)

Analytics answers product and business questions such as feature use, funnel steps, and outcome events. It is
separate from Telemetry: Analytics carries business events; Telemetry carries traces, metrics, logs, exceptions,
and technical health.

## 1. Data flow

```text
Application + Write Key
  → SDK queue / batch / gzip / retry
  → IngestEvents
  → schema validation + sensitive-field redaction + EventId deduplication
  → PostgreSQL retention store
  → Web Explorer / aggregate query / CSV export
```

## 2. Web workflow

Routes: `/analytics/schemas`, `/analytics/explorer`

> **Path naming note:** Web page routes shown in the browser address bar remain under `/analytics/...`, while
> management API requests made in the background use `/api/v1/.../insights/...`. This intentional mismatch keeps
> browser privacy and ad-blocking filters from mistaking event-management queries for tracking traffic. The former
> `/api/v1/.../analytics/...` management API remains as a compatibility alias, and SDK runtime ingestion remains at
> `/api/v1/analytics/events:batch`.

### Event schemas

1. Create a JSON Schema for a stable event name.
2. Set display name, description, and retention from 1 to 3650 days.
3. Mark sensitive properties with `x-asterloom-sensitive: true`.
4. Update and Archive/Restore schemas; producers should stop sending archived events.

### Write keys

- Create a separate Write Key per application, environment, and producer.
- The secret appears only on Create/Rotate; copy it immediately into a secret manager.
- Rotate after exposure and Revoke when a producer is retired.

### Explorer

- Filter by event name, actor, event ID, and time range.
- Inspect redacted properties and context.
- Query event counts and unique actors by interval.
- Export a bounded CSV result.

## 3. C# SDK

```csharp
using Asterloom.Sdk.Analytics;

await using var analytics = new AsterloomAnalyticsClient(
    transport.HttpClient,
    new AsterloomAnalyticsClientOptions
    {
        WriteKey = analyticsWriteKey,
        BatchSize = 20,
        FlushInterval = TimeSpan.FromSeconds(5),
        OfflineQueuePath = offlineQueuePath,
        CommonContext = new Dictionary<string, object?>
        {
            ["platform"] = "win-x64",
            ["version"] = appVersion,
        },
    });

string eventId = await analytics.TrackAsync(
    "checkout.completed",
    new { orderId, amount, currency = "CNY" },
    new AsterloomAnalyticsIdentity(
        ActorId: userId,
        SessionId: sessionId),
    cancellationToken: cancellationToken);

var result = await analytics.FlushAsync(cancellationToken);
```

Defaults are a batch size of 20, a five-second flush interval, three retries, and GZip above 1 KiB.
`DisposeAsync` attempts one final flush within the bounded shutdown timeout.

## 4. Reliability and deduplication

- The SDK assigns a UUIDv7 `EventId`; the server uses it for idempotent deduplication.
- Transient errors use exponential backoff and honor `Retry-After`.
- `TrackAsync` fails when the bounded queue is full rather than consuming unlimited memory.
- `OfflineQueuePath` survives process restart. The current file is not automatically encrypted, so protect its
  directory and add encrypted persistence for sensitive deployments.
- `DeliveryFailed` reports schema/business rejections, while `FlushResult` separates Accepted, Rejected,
  Deduplicated, and Remaining.
- A crash can still lose the final unpersisted events. Analytics is not a financial ledger or sole business source
  of truth.

## 5. Schema and privacy

- The SDK normalizes event names to lowercase with length 2–100.
- Properties must satisfy the active schema. Invalid events are rejected per item rather than failing the batch.
- `x-asterloom-sensitive` redacts before durable storage but is not permission to send arbitrary PII.
- Never send passwords, tokens, payment-card data, private keys, complete request/response bodies, or unrestricted
  personal data.
- Prefer opaque internal actor IDs. Define how a stable anonymous ID joins to an account after sign-in.
- Keep context small and controlled; do not serialize whole user or device objects.

## 6. Permissions and authentication

Runtime ingestion uses `X-Asterloom-Write-Key`, scoped to one Environment. Administration and reads use bearer
tokens and these permissions:

- `analytics.schema.read/create/update/archive/restore`
- `analytics.retention.update`
- `analytics.write-key.read/create/rotate/revoke`
- `analytics.event.read`, `analytics.query.execute`, `analytics.event.export`

A Write Key cannot administer or query events. Never reuse one across Environments.

## 7. Production checklist

- [ ] Register the schema before deploying a producer.
- [ ] Version event names, field types, and units as a data contract.
- [ ] Remove or minimize sensitive data and mark necessary fields for redaction.
- [ ] Isolate Write Keys by application/environment and define rotation.
- [ ] Protect the offline queue and flush during graceful shutdown.
- [ ] Retention satisfies business, privacy, and compliance requirements.
- [ ] Transactional storage, not Analytics, retains critical business state.

## 8. Related implementation

- Runtime protocol: [analytics.proto](../../Proto/Asterloom/analytics/v1/analytics.proto)
- Admin protocol: [analytics_admin.proto](../../Proto/Asterloom/analytics/v1/analytics_admin.proto)
- Types: [analytics_types.proto](../../Proto/Asterloom/analytics/v1/analytics_types.proto)
- SDK: [AsterloomAnalyticsClient.cs](../../Backend/Asterloom.Sdk.Analytics/AsterloomAnalyticsClient.cs)
- Ingestion: [AnalyticsIngestionService.cs](../../Backend/Asterloom.Module.Analytics/AnalyticsIngestionService.cs)
- Schema validator: [AnalyticsSchemaValidator.cs](../../Backend/Asterloom.Module.Analytics/AnalyticsSchemaValidator.cs)
- Web: [analytics-workspace.tsx](../../Frontend/features/analytics/analytics-workspace.tsx)
- Telemetry comparison: [Telemetry.md](Telemetry.md)
