# Feature Flags and Variants

[English](Feature-Flags.md) | [简体中文](Feature-Flags.zh-CN.md) | [Module index](README.md)

Feature enables, disables, or assigns capability variants without shipping another client release. It implements
an OpenFeature C# provider and reuses Targeting segments and deterministic 100,000-bucket allocation.

## 1. Model

Each flag has one fixed value kind: Boolean, String, Integer, Double, or Object. A definition contains:

- `Enabled` and `DefaultVariantKey`
- type-consistent Variants
- optional prerequisite flag and expected variant
- ordered Segment targeting rules
- optional bucket allocations and stable salt
- Draft and Published revisions

Runtime reads only the Published Definition; editing a Draft does not change user behavior.

## 2. Web workflow

Route: `/features`

1. Select Tenant, Application, and Environment.
2. Create a stable flag key, value kind, and initial variants.
3. Edit the Draft default, prerequisites, Segment rules, and percentage allocation.
4. Validate and resolve type, reference, interval, or dependency-cycle errors.
5. Simulate representative Evaluation Contexts.
6. Publish an immutable runtime revision.
7. Review revisions and Rollback, Archive, or Restore as needed.

Rollback publishes a new revision from a historical definition and retains subsequent audit history.

## 3. Evaluation order

```text
Active + Published
  → Enabled?
  → Prerequisites
  → ordered Segment rules
  → stable bucket allocation
  → default variant
```

The result includes value, variant key, revision, reason, trace, bucket, and bucketing version. For archived,
unpublished, type-mismatched, or invalid-context flags, the SDK returns the caller's safe default with OpenFeature
error details.

## 4. C# / OpenFeature integration

```csharp
using Asterloom.Sdk.Feature;
using OpenFeature.Model;

var provider = new AsterloomFeatureProvider(
    transport.CallInvoker,
    new AsterloomFeatureProviderOptions
    {
        Scope = new AsterloomFeatureScope(tenantId, applicationId, environmentId),
        CacheDuration = TimeSpan.FromSeconds(30),
        LastKnownGoodDuration = TimeSpan.FromHours(24),
    });

var context = EvaluationContext.Builder()
    .SetTargetingKey(installationId)
    .Set("userId", userId)
    .Set("clientVersion", appVersion)
    .Set("platform", "win-x64")
    .Set("region", "CN")
    .Build();

var result = await provider.ResolveBooleanValueAsync(
    "new-checkout",
    defaultValue: false,
    context,
    cancellationToken);

bool enabled = result.Value;
```

The provider also implements String, Integer, Double, and Structure resolvers. `targetingKey` is required, and
custom context attributes must be String, Boolean, or Number.

## 5. Cache and failure behavior

- Successful results cache for 30 seconds by default, isolated by flag, type, and context.
- On a temporary RPC failure, the provider can return the latest success for the default 24-hour Last-Known-Good
  window.
- With no valid cache, it returns the code default so a flag outage need not prevent startup.
- Shorten cache time for high-risk kill switches according to business RTO and test offline semantics.
- Call `ClearCache()` when an immediate refresh is required, not before every evaluation.

## 6. Administration automation

`AsterloomFeatureAdminClient` covers List/Get/Create, Draft Update, Validate, Publish, Revisions, Rollback,
Archive/Restore, and Simulation. Business applications normally need only `AsterloomFeatureProvider`; do not
grant publish permission to endpoint clients.

## 7. Permissions

- `feature.flag.read/create/update/validate/publish/rollback/archive/restore/evaluate`
- `feature.revision.read`
- `feature.simulation.execute`

A Feature Flag is not a security boundary. Sensitive server operations still require Authorization even when a UI
flag is false.

## 8. Implementation rules

- Keep flag keys stable and centrally defined; drain traffic and observe before removing code.
- Supply a conservative default on every call.
- Keep all variants in one value kind; do not mutate a published flag's type contract.
- Keep prerequisite graphs acyclic and shallow.
- Put no PII or tokens in context; Analytics should record only necessary flag, variant, and revision fields.
- Exercise Disabled, Prerequisite Fail, Segment Match, Bucket, and Default paths before publication.

## 9. Related implementation

- Runtime protocol: [feature.proto](../../Proto/Asterloom/feature/v1/feature.proto)
- Admin protocol: [feature_admin.proto](../../Proto/Asterloom/feature/v1/feature_admin.proto)
- Types: [feature_types.proto](../../Proto/Asterloom/feature/v1/feature_types.proto)
- Evaluation service: [FeatureEvaluationService.cs](../../Backend/Asterloom.Module.Feature/FeatureEvaluationService.cs)
- OpenFeature provider: [AsterloomFeatureProvider.cs](../../Backend/Asterloom.Sdk.Feature/AsterloomFeatureProvider.cs)
- Admin SDK: [AsterloomFeatureAdminClient.cs](../../Backend/Asterloom.Sdk.Feature/AsterloomFeatureAdminClient.cs)
- Web: [feature-workspace.tsx](../../Frontend/features/feature/feature-workspace.tsx)
- Targeting: [Targeting-Rollout.md](Targeting-Rollout.md)
