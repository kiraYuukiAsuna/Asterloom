# Dynamic Configuration and Snapshots

[English](Dynamic-Config.md) | [简体中文](Dynamic-Config.zh-CN.md) | [Module index](README.md)

Dynamic Config changes application behavior without a new release. It provides typed entries, Draft/Publish,
targeted values, immutable revisions, Environment snapshots, ETags, update checks, and Last-Known-Good behavior.

## 1. Configuration model

| Field | Meaning |
| --- | --- |
| Value Kind | Boolean, Integer (`long`), Double, String, or JSON |
| Visibility | Client or Server |
| Schema JSON | Optional value constraint, especially for JSON |
| Default Value | Used when no targeting rule matches |
| Targeting Rules | Ordered Segment-to-value mappings |
| Draft / Published | Separates editing from runtime state |
| Snapshot Version | Current published configuration-set version for an Environment |

`Server` visibility means an entry is omitted from a normal Client Snapshot; it is still not a secret vault.
Passwords, private keys, tokens, and database connection strings belong in a secret manager.

## 2. Web workflow

Route: `/config`

1. Create a stable key and select value kind and visibility.
2. Configure default, schema, and optional Segment-targeted values.
3. Validate the Draft and review Diff/Changed Paths.
4. Preview effective values with representative contexts.
5. Publish a new immutable revision and Environment snapshot.
6. Review revision/snapshot history and Rollback when required.
7. Archive or Restore entries through their lifecycle.

Runtime returns only Published, Active entries permitted by visibility. Draft changes never leak before Publish.

## 3. Snapshots and ETags

- A Snapshot captures an Environment's published entry set after a change.
- The client resolves effective values by scope, visibility, and targeting context.
- ETags include context effects, so targeted users can have different ETags.
- The SDK sends `If-None-Match`; an unchanged server response is Not Modified.
- `CheckForUpdatesAsync` compares snapshot versions and allows a later full fetch.
- `SnapshotUpdated` fires when the cached version actually changes.

## 4. C# integration

```csharp
using Asterloom.Sdk.Config;

var scope = new AsterloomConfigScope(tenantId, applicationId, environmentId);
using var config = new AsterloomConfigClient(
    transport.HttpClient,
    new AsterloomConfigClientOptions
    {
        Scope = scope,
        CacheDuration = TimeSpan.FromSeconds(30),
        LastKnownGoodDuration = TimeSpan.FromHours(24),
    });

var context = AsterloomConfigContext.Create(
    scope,
    targetingKey: installationId,
    userId: userId,
    clientVersion: appVersion,
    platform: "win-x64",
    region: "CN");

string endpoint = await config.GetStringAsync(
    "checkout.endpoint",
    defaultValue: "https://fallback.example/",
    context,
    cancellationToken);

var status = await config.CheckForUpdatesAsync(
    context,
    knownSnapshotVersion,
    cancellationToken);
```

The SDK exposes `GetBooleanAsync`, `GetInt64Async`, `GetDoubleAsync`, `GetStringAsync`, and `GetJsonAsync<T>`.
A missing key returns the caller default. An existing key with a different type throws
`AsterloomConfigValueTypeException` to expose a contract error.

## 5. Cache and Last-Known-Good

- Defaults are a 30-second cache and 24-hour Last-Known-Good window, configurable up to 30 days.
- On network, JSON, or timeout failure, a snapshot inside the LKG window returns with `IsLastKnownGood=true`.
- With no usable LKG, `AsterloomConfigUnavailableException` is thrown; the application still needs safe local
  defaults.
- The default memory cache disappears on restart. Offline-start desktop apps should implement an encrypted,
  atomic, integrity-checked `IAsterloomConfigSnapshotCache`.

## 6. Server values

A backend that genuinely needs Server visibility must:

1. Receive `config.snapshot.server.read`.
2. Set `AllowServerValues = true` in `AsterloomConfigClientOptions`.
3. Call `GetSnapshotAsync(..., includeServerValues: true)`.

Do not grant that permission to desktop or browser identities. A Server value still must not contain a real
secret.

## 7. Permissions

- Administration: `config.entry.read/create/update/validate/publish/rollback/archive/restore`
- Inspection: `config.diff.read`, `config.revision.read`, `config.preview.execute`
- Runtime: `config.snapshot.read`, `config.snapshot.server.read`, `config.update.check`
- History: `config.snapshot.history.read`

## 8. Implementation rules

- Treat key and value kind as a published API contract; do not mutate types in place.
- Supply conservative defaults and distinguish missing keys from type errors.
- Before production Publish, run Validate, Diff, Preview, and representative-context tests.
- Keep values small; use File Storage for large files and models.
- Avoid aggressive polling; use cache, ETag, and a reasonable check interval.
- Rollback creates a new snapshot that clients observe through cache expiry or update checks.

## 9. Related implementation

- Runtime protocol: [config.proto](../../Proto/Asterloom/config/v1/config.proto)
- Admin protocol: [config_admin.proto](../../Proto/Asterloom/config/v1/config_admin.proto)
- Types: [config_types.proto](../../Proto/Asterloom/config/v1/config_types.proto)
- SDK: [AsterloomConfigClient.cs](../../Backend/Asterloom.Sdk.Config/AsterloomConfigClient.cs)
- Evaluation service: [ConfigEvaluationService.cs](../../Backend/Asterloom.Module.Config/ConfigEvaluationService.cs)
- Management service: [ConfigManagementService.cs](../../Backend/Asterloom.Module.Config/ConfigManagementService.cs)
- Web: [config-workspace.tsx](../../Frontend/features/config/config-workspace.tsx)
