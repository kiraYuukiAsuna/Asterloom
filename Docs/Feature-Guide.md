# Asterloom Feature Usage Guide

[English](Feature-Guide.md) | [简体中文](Feature-Guide.zh-CN.md)

This guide explains how to operate every Asterloom capability from the Web
Console and how a .NET application consumes the runtime C# SDKs. It describes
the implementation in this repository, not a future multi-language SDK. For
capability boundaries, permissions, complete workflows, and implementation links,
use the [module guides](Module/README.md).

## 1. Access paths

| Consumer | Recommended path | Purpose |
| --- | --- | --- |
| Administrator | Browser → Next.js Web Console/BFF → HTTP/JSON | Configure and inspect every management capability. |
| .NET desktop application | C# SDK → native gRPC or HTTP transfer endpoints | Login, evaluate flags/config, check updates, send analytics, and transfer files. |
| .NET backend service | Client Credentials → authenticated C# SDK transport | Service-to-service authorization and runtime calls. |
| Third-party HTTP client | Generated OpenAPI client or documented JSON Transcoding route | Use the same Protobuf contract through HTTP/JSON. |

`Proto/Asterloom` is the contract source. Custom business RPCs use
`google.api.http`, so native gRPC and JSON Transcoding execute the same server
implementation. A browser should normally call `/api/asterloom/*` on the
Next.js BFF instead of receiving an Asterloom access token.

## 2. Scope model and recommended setup order

Most runtime resources belong to this hierarchy:

```text
Tenant
  └─ Application
       └─ Environment
            ├─ Targeting segments
            ├─ Feature flags
            ├─ Dynamic configuration
            ├─ Desktop releases
            ├─ Analytics
            └─ Telemetry
```

Use stable IDs for SDK configuration. Slugs are human-readable identifiers for
management and search; IDs are the authoritative scope values returned by the
API.

For a new product, configure capabilities in this order:

1. Create a tenant, application, and environment.
2. Invite administrators and register interactive or service OIDC clients.
3. Create roles, bindings, and any explicit allow/deny policies.
4. Create reusable targeting segments.
5. Publish feature flags and dynamic configuration.
6. Create storage buckets and release channels/signing keys.
7. Create analytics schemas/write keys and telemetry sources/settings.
8. Verify the result in each simulator, Operations, Audit, and the reference
   application doctor.

## 3. Start the local platform

Prerequisites are Docker with Compose, or .NET SDK 10.0.400 and Node.js 24 when
running components directly.

```powershell
Copy-Item Deploy/.env.example .env
docker compose up --build
```

Open `http://localhost:60000` and sign in with the local-only administrator from
`.env`. The checked-in example defaults are `admin@asterloom.local` and
`Asterloom-Local-Admin!2026`; never reuse them outside local development.

The local endpoints are:

| Endpoint | Address |
| --- | --- |
| Web Console/BFF | `http://localhost:60000` |
| Server HTTP/JSON and Passport | `http://localhost:60001` |
| Server native gRPC | `http://localhost:60002` |
| MinIO S3 API | `http://localhost:60003` |
| Reference HTTP / gRPC | `http://localhost:60004` / `http://localhost:60005` |
| PostgreSQL, Redis, MinIO Console, OTLP, Collector health | Not published; Compose-network only |

## 4. Web Console workflows

Every management action below is backed by an Admin RPC and is covered by the
API/UI coverage check and Playwright journey.

### 4.1 Platform resources

Route: `/tenants`

1. Create a tenant with a unique slug and display name.
2. Select it, then create an application.
3. Select the application and create Development, Staging, or Production
   environments. Mark sensitive environments as protected when appropriate.
4. Add or remove tenant memberships by subject and role.
5. Use Edit, Archive, Restore, search, and pagination to manage lifecycle.

Archiving is the normal reversible removal operation. A protected or archived
parent can prevent writes to its descendants.

### 4.2 Identity / Passport

Route: `/identity/users`

- **Users:** create or invite a global account, resend an invitation, edit profile fields,
  assign Passport roles, reset passwords, suspend/reactivate, and archive/restore.
- **Sessions:** inspect a user's sessions, revoke one session, or revoke all
  sessions.
- **Application memberships:** attach, restore, filter, or remove a global account independently per application.
- **OIDC clients:** create public native clients or confidential service/Web
  clients, bind one to a Platform Application, configure registration/auto-join,
  redirects/grants/scopes, rotate a secret, and delete a client.
- **OIDC scopes:** create, update, and delete scopes exposed by Passport.

Use Authorization Code + S256 PKCE for interactive desktop/management Web clients and Client Credentials for
backend services. A controlled Password Flow is permitted only for a confidential, application-bound business
backend implementing its own end-user login page; browser JavaScript must never call it. Do not use Implicit Flow.
See [business application identity integration](Module/Identity-Business-Integration.md). Copy a newly issued client
secret immediately and store it in a secret manager.

### 4.3 Authorization

Route: `/authorization/roles`

1. Review the permission catalog.
2. Create a role and assign permission keys.
3. Bind the role to an actor globally or at tenant/application/environment
   scope.
4. Add explicit policy rules when role bindings are insufficient. Deny rules
   take precedence where the policy model specifies a conflict.
5. Inspect policy revisions and use the simulator before deploying a sensitive
   change.
6. Use the runtime permission check for the final server-side decision.

Hiding a Web button is only a user-experience optimization. Every protected
operation must still call server-side authorization.

### 4.4 Targeting and rollout

Route: `/targeting/segments`

- Inspect supported attributes and operators.
- Create a segment from typed rules, update it, archive it, or restore it.
- Run the simulator with a targeting key and attributes such as user ID,
  client version, platform, region, and language.
- Use the bucket preview to verify stable percentage allocation.

Feature, Config, and Release all share this targeting context and stable
bucketing implementation. Use the same targeting key for the same subject when
you need consistent allocation across requests.

### 4.5 Feature flags

Route: `/features`

1. Select tenant, application, and environment.
2. Create a typed flag and define variants/defaults.
3. Edit the draft with prerequisites, targeting rules, and percentage
   allocations.
4. Validate the draft, then publish it.
5. Use Simulate for an explainable control-plane result and Evaluate for the
   runtime result.
6. Inspect revisions, roll back to a previous revision, or archive/restore the
   flag.

Only a published revision is visible to runtime evaluation. Always provide a
safe SDK default and treat targeting attributes as application data, not as a
security boundary.

### 4.6 Dynamic configuration

Route: `/config`

1. Create a Boolean, integer, double, string, or JSON entry.
2. Edit the draft value and optional targeted values.
3. Validate and inspect the draft diff before publishing.
4. Preview the effective value with a representative targeting context.
5. Publish, inspect revisions/snapshots, check for updates, or roll back.
6. Archive/restore entries that should no longer be returned.

Client caches and Last-Known-Good behavior keep a previously verified snapshot
available during a temporary network failure. Secrets must use a dedicated
secret manager, not Dynamic Config.

### 4.7 Desktop releases and updates

Routes: `/channels`, `/artifacts`, `/releases`

This capability also defines RIDs, Velopack package structure, initial installers, external signatures, and the
upload protocol. Read the [Asterloom Desktop Update Guide](Module/Desktop-Updates.md) before implementation; an ordinary
publish directory or arbitrary ZIP is not an installable artifact.

1. Create a channel such as `stable`, `beta`, or `canary`.
2. Generate an RSA signing key outside Asterloom. Register only the public key
   and retain the private key in controlled build/signing infrastructure.
3. Upload an artifact, verify its SHA-256, sign the expected digest with
   RSA-PSS, and complete the upload ticket.
4. Create a release draft, attach verified artifacts, set minimum version and
   rollout basis points (`100000` = 100%).
5. Validate the manifest, sign its digest, and publish.
6. Simulate an update, then pause, promote, or roll back the release as needed.

The C# client verifies the trusted public-key fingerprint, manifest signature,
artifact signature, and SHA-256 before exposing downloaded bytes. For an actual
desktop install/restart, package with Velopack and pass
`AsterloomVelopackUpdateSource` to Velopack's `UpdateManager`.

### 4.8 Analytics

Routes: `/analytics/schemas`, `/analytics/explorer`

1. Create an event JSON Schema and mark sensitive properties with
   `x-asterloom-sensitive: true`.
2. Set retention and create a write key for the producer.
3. Copy the write-key secret when created or rotated; revoke it when no longer
   needed.
4. Send events with the Analytics SDK.
5. Inspect redacted events, run aggregate queries, and export filtered CSV.

Analytics represents product behavior and business outcomes. Avoid sending
passwords, tokens, card data, or unrestricted PII even when redaction is
configured.

### 4.9 Telemetry

Routes: `/telemetry/sources`, `/telemetry/health`

- Register service sources and resource attributes.
- Configure trace sampling, OTLP gRPC or HTTP/protobuf exporter endpoint, and
  the diagnostics base URL.
- Check Collector health and recent errors.
- Generate a trace diagnostic link from a trace ID.

Telemetry is for traces, metrics, logs, exceptions, and technical health. It is
separate from Analytics product events.

### 4.10 File storage

Routes: `/storage/buckets`, `/storage/objects`

The Storage sidebar opens `/storage/objects` by default. Bucket creation is under the **Buckets** tab at the top
of the workspace, in the **Create bucket** card. Administrators normally create a logical bucket in the Web
console first; applications then upload and download through the SDK/API with its configured `BucketId`. The Web
console also supports manual upload, download, copy, and deletion.

1. Create and configure a logical bucket.
2. Start an upload session, upload bytes to the short-lived signed transfer
   URL with every required header, then complete the session.
3. Inspect object metadata and verified SHA-256.
4. Generate a short-lived download URL, copy an object, edit custom metadata,
   or delete an object.
5. Archive/restore buckets as their lifecycle changes.

The object transfer URL may point to an S3 origin rather than the Asterloom API
origin. Never attach an Asterloom Bearer token to that URL; use only the signed
headers returned in the transfer ticket.

See the [Asterloom File Storage Guide](Module/File-Storage.md) for the resource model, complete three-phase transfer
protocol, permissions, and SDK coverage boundary.

### 4.11 Operations, Audit, and appearance

- `/operations/apis`: browse the gRPC/HTTP API catalog and download OpenAPI.
- `/operations/health`: inspect platform and dependency health.
- `/audit`: search, filter, inspect/correlate, and export immutable audit
  events.
- The theme control supports Light, Dark, and System modes and persists the
  selection.

Use Operations for deployment health and Audit for who changed what. Neither
is a replacement for Telemetry traces/logs.

## 5. C# SDK integration

The SDKs currently live as projects in this repository. Until packages are
published to a NuGet feed, reference only the capabilities your application
uses. From the repository root, for example:

```powershell
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Identity\Asterloom.Sdk.Identity.csproj
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Rpc\Asterloom.Sdk.Rpc.csproj
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Feature\Asterloom.Sdk.Feature.csproj
```

The complete, executable integration is in
`Backend/Samples/Asterloom.ReferenceApp.Client`.

### 5.1 Service login and shared authenticated transport

```csharp
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = builder.Configuration["Asterloom:ClientId"]!;
    options.ClientSecret = builder.Configuration["Asterloom:ClientSecret"]!;
    options.EnableServiceCredentials = true;
});

using var host = builder.Build();
await host.StartAsync();
var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
await identity.GetServiceAccessTokenAsync();

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);
```

`transport.CallInvoker` is used by gRPC SDKs. `transport.HttpClient` is used by
JSON/transfer SDKs. The Bearer handler only sends tokens to the configured
Asterloom origin and skips signed S3 transfer URLs.

### 5.2 Interactive Passport login

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = "my-desktop-client";
    options.EnableInteractiveAuthentication = true;
    options.RequestRefreshTokens = true;
});

var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
var tokens = await identity.SignInAsync(cancellationToken: cancellationToken);
var accessToken = await identity.GetAccessTokenAsync(cancellationToken);
await identity.SignOutAsync(cancellationToken);
```

The SDK opens the system browser and uses Authorization Code + PKCE. Replace
the in-memory token store with an OS-protected implementation for a production
desktop application.

### 5.3 Authorization

```csharp
using Asterloom.Sdk.Authorization;

var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
var decision = await authorization.CheckPermissionAsync(
    "feature.flag.evaluate",
    new AsterloomAuthorizationScope(tenantId, applicationId, environmentId),
    cancellationToken);

if (!decision.Allowed)
{
    throw new UnauthorizedAccessException(decision.Reason);
}
```

### 5.4 Targeting and Feature Flags

```csharp
using Asterloom.Sdk.Feature;
using OpenFeature.Model;

var provider = new AsterloomFeatureProvider(
    transport.CallInvoker,
    new AsterloomFeatureProviderOptions
    {
        Scope = new AsterloomFeatureScope(tenantId, applicationId, environmentId),
    });

var context = EvaluationContext.Builder()
    .SetTargetingKey(userId)
    .Set("region", "CN")
    .Set("clientVersion", appVersion)
    .Build();

var result = await provider.ResolveBooleanValueAsync(
    "new-checkout",
    defaultValue: false,
    context,
    cancellationToken);

bool enabled = result.Value;
```

Use `AsterloomTargetingAdminClient` for management automation and
`AsterloomTargetingEvaluator.ComputeBucket` only when a local deterministic
bucket calculation is explicitly required.

### 5.5 Dynamic Config

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
    targetingKey: userId,
    clientVersion: appVersion,
    platform: "win-x64",
    region: "CN");

string endpoint = await config.GetStringAsync(
    "checkout.endpoint",
    "https://fallback.example/",
    context,
    cancellationToken);
```

Typed getters are available for Boolean, `long`, `double`, string, and JSON.
Subscribe to `SnapshotUpdated` or call `CheckForUpdatesAsync` when the
application needs an update signal.

### 5.6 Desktop updates

```csharp
using Asterloom.Sdk.Release;

var scope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
using var releases = new AsterloomReleaseClient(
    transport.HttpClient,
    new AsterloomReleaseClientOptions
    {
        Scope = scope,
        TargetRuntimeId = "win-x64",
        PackageId = "my-desktop-app",
        TrustedPublicKeysByFingerprint = trustedReleaseKeys,
    });

var decision = await releases.CheckForUpdateAsync(
    "stable",
    currentVersion,
    AsterloomReleaseContext.Create(
        scope,
        targetingKey: installationId,
        clientVersion: currentVersion,
        platform: "win-x64"),
    cancellationToken);

if (decision.UpdateAvailable)
{
    await releases.DownloadToFileAsync(
        decision,
        destinationPath,
        cancellationToken: cancellationToken);
}
```

Do not bypass `AsterloomReleaseClient` verification by downloading the artifact
URL yourself.

For RIDs such as `win-x64`, Velopack packaging, artifact/manifest signing, and the release sequence, see the
[Asterloom Desktop Update Guide](Module/Desktop-Updates.md).

### 5.7 Analytics

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

await analytics.TrackAsync(
    "checkout.completed",
    new { orderId, amount },
    new AsterloomAnalyticsIdentity(
        ActorId: userId,
        SessionId: sessionId),
    cancellationToken: cancellationToken);

var flush = await analytics.FlushAsync(cancellationToken);
```

Call `FlushAsync` during a graceful shutdown. `DisposeAsync` also attempts a
bounded final flush.

### 5.8 Telemetry

```csharp
using Asterloom.Sdk.Telemetry;

var telemetry = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    serviceName: "my-company.checkout",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString());

telemetry.TenantId = tenantId.ToString("D");
telemetry.ApplicationId = applicationId.ToString("D");
telemetry.EnvironmentId = environmentId.ToString("D");
telemetry.ActivitySourceNames.Add("MyCompany.Checkout");
telemetry.MeterNames.Add("MyCompany.Checkout");

builder.Services.AddAsterloomTelemetry(telemetry);
builder.Logging.AddAsterloomTelemetryLogging(telemetry);
```

Configure `OTEL_EXPORTER_OTLP_ENDPOINT` for the Collector. Keep metric labels
low-cardinality; put request/user-specific detail in controlled traces or logs.

### 5.9 Storage

```csharp
using System.Security.Cryptography;
using Asterloom.Sdk.Storage;

using var storage = new AsterloomStorageClient(
    transport.HttpClient,
    new AsterloomStorageClientOptions
    {
        Scope = new AsterloomStorageScope(tenantId),
    });

byte[] bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

var stored = await storage.UploadAsync(
    new AsterloomStorageUploadRequest(
        bucketId,
        ObjectKey: $"documents/{Path.GetFileName(sourcePath)}",
        FileName: Path.GetFileName(sourcePath),
        ContentType: "application/octet-stream",
        SizeBytes: bytes.LongLength,
        Sha256: sha256,
        ApplicationId: applicationId,
        EnvironmentId: environmentId),
    new MemoryStream(bytes, writable: false),
    cancellationToken);

await using var destination = File.Create(destinationPath);
await storage.DownloadToAsync(stored, destination, cancellationToken: cancellationToken);
```

The current `AsterloomStorageClient` wraps object upload and download. Bucket administration and Object List,
Metadata, Copy, and Delete are covered by the gRPC/JSON API and Web console; use a generated API client when an
application needs them. See the [Asterloom File Storage Guide](Module/File-Storage.md) for the complete model.

### 5.10 Native gRPC, HTTP/JSON, and persistence

An application-specific backend can expose one Protobuf service through both
transports:

```csharp
builder.Services.AddGrpc().AddJsonTranscoding();

var app = builder.Build();
app.MapGrpcService<MyGrpcService>();
```

Add `google.api.http` to each custom RPC. .NET clients use the generated gRPC
client; browsers and ordinary HTTP clients call its JSON route. Do not build a
second MVC controller with duplicated business logic.

Persistence is not a generic remote database API. Asterloom modules persist
their own data in module-owned PostgreSQL schemas. Your service persists its
own durable business data with Npgsql, for example:

```csharp
builder.Services.AddSingleton(_ =>
    Npgsql.NpgsqlDataSource.Create(
        builder.Configuration.GetConnectionString("Application")!));
```

Keep application tables in an application-owned schema and do not read
Asterloom module schemas directly.

## 6. Full-capability reference application

The reference application is the executable example for the snippets above:

```powershell
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- provision
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- doctor
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- login
$env:ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD = "Use-A-Strong-Test-Password!2026"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- account-demo user@example.com "Example User"
```

- `provision` creates an isolated, complete capability set.
- `doctor` independently verifies Identity, Authorization, Targeting, Feature
  and Rollout, Config, Release, Analytics, Telemetry, native gRPC, JSON
  Transcoding, Storage, PostgreSQL persistence, and Operations/OpenAPI.
- `login` verifies interactive Passport Authorization Code + PKCE.
- `account-demo` verifies global account registration, confirmation, application membership, business BFF sign-in, and sign-out.

See [Reference-Application.md](Reference-Application.md) for environment
variables, production Compose commands, and the diagnostic contract.

## 7. Verification commands

```powershell
dotnet restore Backend/Asterloom.sln
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln

Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run test:e2e
```

Production E2E uses `npm run test:e2e:production` with credentials supplied
through `ASTERLOOM_E2E_*` environment variables. Never commit those values or
run mutating production tests without an isolated naming/cleanup policy.

## 8. Production safety checklist

- Use HTTPS and a fixed OIDC issuer.
- Store signing keys, client secrets, write keys, Redis credentials, session
  encryption keys, database credentials, and S3 credentials in a secret
  manager.
- Use Redis for production BFF sessions; `memory` is development-only.
- Keep browser cookies Secure, HttpOnly, SameSite-aware, and opaque.
- Apply migrations as an explicit deployment step before starting the Server.
- Back up PostgreSQL and object storage and test restore procedures.
- Configure short transfer URL lifetimes, least privilege, rate limits, and
  audit retention.
- Verify `/health/live`, `/health/ready`, `/health/startup`, Operations, and the
  reference `doctor` after deployment.
