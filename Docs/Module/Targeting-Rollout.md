# Targeting and Deterministic Rollout

[English](Targeting-Rollout.md) | [简体中文](Targeting-Rollout.zh-CN.md) | [Module index](README.md)

Targeting is the shared audience-rule and deterministic-bucketing engine used by Feature, Dynamic Config, and
Desktop Release. A Segment answers whether a context belongs to an audience; Rollout answers whether a stable
subject falls into a percentage interval.

## 1. Evaluation Context

| Field | Purpose |
| --- | --- |
| `targetingKey` | Required stable subject key used for bucketing |
| `userId` | Optional account ID |
| `applicationId` / `environmentId` | Resource scope |
| `clientVersion` | Semantic-version conditions |
| `platform` | Targeting attribute such as `win-x64`; it does not select a Release artifact |
| `region` / `language` | Region and language rules |
| Custom Attributes | Up to 64 Text/Truth/Numeric attributes |

Choose a stable, non-sensitive installation ID, user ID, or service key for `targetingKey`. Recreating it on each
launch changes rollout membership. PII-like custom attribute names such as email, names, phone numbers, and raw
device IDs are rejected. The server cannot detect PII hidden in arbitrary values, so callers must still minimize and
de-identify them.

## 2. Segment rules

Route: `/targeting/segments`

A Segment has a stable key and one flat rule in an Environment. The rule combines 1–50 conditions with `ALL` or
`ANY` and short-circuits in declaration order.

Principal operators:

- Text: Equals, NotEquals, OneOf, Contains, StartsWith, EndsWith.
- Numeric: Equals and comparison operators.
- Boolean: Equals and NotEquals.
- Presence: Exists and NotExists.
- Version: SemanticVersionEquals/GreaterThan/LessThan.

The Web console covers the Attribute/Operator Catalog, Segment List/Get/Create/Update/Archive/Restore, and
Simulation. A simulation explains Matched, Missing Attribute, Type Mismatch, and other condition outcomes.

## 3. Bucketing v1

```text
material = UTF8("v1" + NUL + namespace + NUL + salt + NUL + targetingKey)
hash     = SHA-256(material)
value    = first 8 hash bytes as unsigned big-endian UInt64
bucket   = value mod 100000
```

- There are `100000` buckets; 1% is `1000`, and 12.5% is `12500`.
- Allocation intervals are left-closed and right-open `[start, end)` and cannot overlap.
- Namespace includes resource type, resource key, and Environment to prevent accidental cross-resource sharing.
- Salt must remain stable. Changing it reshuffles everyone and is an explicit breaking operation.
- Server and C# SDK return the same bucket for identical namespace, salt, and targeting key.

## 4. C# administration and preview

```csharp
using Asterloom.Sdk.Targeting;

var admin = new AsterloomTargetingAdminClient(transport.CallInvoker);
var scope = new AsterloomTargetingScope(tenantId, applicationId, environmentId);
var catalog = await admin.ListTargetingAttributesAsync(cancellationToken);
var segments = await admin.ListSegmentsAsync(scope, cancellationToken: cancellationToken);

uint bucket = AsterloomTargetingEvaluator.ComputeBucket(
    resourceType: "feature",
    resourceKey: "checkout-v2",
    environmentId: environmentId,
    salt: "stable-salt",
    targetingKey: installationId);
```

Use `AsterloomTargetingAdminClient` for control-plane work. Normal Feature, Config, and Release evaluation should
go through their respective SDKs rather than duplicating rules and hashing in every application.

## 5. Relationship to other modules

- Feature Flag: a Segment matches a rule and bucketing selects a Variant.
- Dynamic Config: a Segment selects a targeted value.
- Desktop Release: a Segment limits the audience and rollout basis points control percentage.
- Analytics: record the final variant/reason when useful, but not the complete context.

Archiving a Segment invalidates or stops matching dependent configurations. Before production changes, exercise
matching, non-matching, missing-attribute, and bucket-boundary cases in each consuming module's simulator.

## 6. Permissions

- `targeting.attribute.read`
- `targeting.segment.read/create/update/archive/restore`
- `targeting.simulation.execute`

Runtime permissions belong to consuming modules, such as `feature.flag.evaluate` or `release.update.check`.

## 7. Production checklist

- [ ] `targetingKey` is stable and persisted for the installation or account lifetime.
- [ ] Context contains no PII, tokens, or unrestricted high-cardinality values.
- [ ] Semantic-version fields contain valid version strings.
- [ ] Rollout is promoted gradually while observing Telemetry and Analytics.
- [ ] Web simulation agrees with C# golden vectors.
- [ ] TypeScript and business clients do not implement independent bucketing algorithms.

## 8. Related implementation

- Admin protocol: [targeting_admin.proto](../../Proto/Asterloom/targeting/v1/targeting_admin.proto)
- Types: [targeting_types.proto](../../Proto/Asterloom/targeting/v1/targeting_types.proto)
- Shared algorithm: [TargetingCore.cs](../../Backend/Asterloom.Shared/Targeting/TargetingCore.cs)
- C# evaluator: [AsterloomTargetingEvaluator.cs](../../Backend/Asterloom.Sdk.Targeting/AsterloomTargetingEvaluator.cs)
- Admin SDK: [AsterloomTargetingAdminClient.cs](../../Backend/Asterloom.Sdk.Targeting/AsterloomTargetingAdminClient.cs)
- Web: [targeting-workspace.tsx](../../Frontend/features/targeting/targeting-workspace.tsx)
