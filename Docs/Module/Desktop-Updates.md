# Asterloom Desktop Update Guide

[English](Desktop-Updates.md) | [简体中文](Desktop-Updates.zh-CN.md) | [Module index](README.md)

This guide describes the current C# desktop update path from initial packaging through signed upload, rollout,
download, installation, and release control. “Desktop update” means an application installed with Velopack;
services, containers, and Web applications should continue to use their normal CI/CD deployment systems.

## 1. Responsibility boundary

```text
CI / external signer
  └─ build the app and create an installer plus .nupkg with Velopack
       └─ sign the artifact SHA-256 with an external RSA-PSS key
            └─ upload to Asterloom and sign the candidate release manifest
                 └─ publish to stable / beta / canary
                      └─ authenticated clients check with a stable targeting key
                           └─ Asterloom selects an eligible, trusted artifact
                                └─ Velopack downloads, replaces files, and restarts the app
```

| Component | Owns | Does not own |
| --- | --- | --- |
| Asterloom Release | Channels, artifact metadata, trust, signed manifests, version comparison, targeting, deterministic rollout, transfer tickets, pause/promote/rollback control | Installer generation, replacing in-use files, process restart |
| Velopack | Initial installer, `.nupkg`, update orchestration, delta reconstruction, file replacement, restart, and install hooks | Asterloom authentication, authorization, targeting, or manifest signing |
| CI/HSM/external signer | Private-key custody, artifact and manifest signatures, optional OS code signing | Sending private keys to Asterloom or desktop clients |

Distribute the Velopack Setup/Installer for the first install. The `.nupkg` stored by Asterloom is for updates to
an already installed application and is not a replacement for the initial installer.

## 2. The three different meanings of platform

### 2.1 Platform resource hierarchy

The Asterloom Platform module defines business isolation, not an operating system:

```text
Tenant
  └─ Application
       └─ Environment
            ├─ Feature / Config / Targeting
            ├─ Release Channel / Desktop Release
            └─ Analytics / Telemetry and other scoped resources
```

- A Tenant is an organization or customer boundary.
- An Application is a product, such as `my-desktop-app`.
- An Environment is a deployment stage such as `development`, `staging`, or `production`.

Normally create one Application for one desktop product and attach artifacts for all supported runtimes to the
same Desktop Release.

### 2.2 Release `targetRuntimeId`

`targetRuntimeId` describes the OS and CPU architecture supported by one artifact. The current backend stores a
lowercase .NET RID-like value and performs an **exact string match**. A `win-x64` client can only receive a
`win-x64` artifact.

Recommended values:

| OS | Architecture | `targetRuntimeId` |
| --- | --- | --- |
| Windows | x64 | `win-x64` |
| Windows | Arm64 | `win-arm64` |
| Windows | x86 | `win-x86` |
| macOS | Intel x64 | `osx-x64` |
| macOS | Apple Silicon | `osx-arm64` |
| Linux | x64 | `linux-x64` |
| Linux | Arm64 | `linux-arm64` |

The field is not a database enum, but it accepts only 1–100 lowercase letters, digits, dots, and hyphens and is
intended to follow .NET RID conventions. Do not mix aliases such as `windows-x64`, `Win64`, or `win_x64`.

Treat the RID as build-artifact metadata rather than guessing it from an operating-system display name. For a
single-RID installer, injecting a fixed CI value is the safest choice. `RuntimeInformation.RuntimeIdentifier` is
also usable, but the release build must assert that it exactly matches the uploaded artifact's `targetRuntimeId`.

A single `1.4.0` release can contain:

```text
my-app-1.4.0-win-x64-full.nupkg   → win-x64 / Full
my-app-1.4.0-win-arm64-full.nupkg → win-arm64 / Full
my-app-1.4.0-osx-arm64-full.nupkg → osx-arm64 / Full
```

Provide at least one Full artifact per release version and runtime. A matching Delta artifact may be added for an
exact source version.

### 2.3 Targeting Context `platform`

The `platform` passed to `AsterloomReleaseContext.Create` is a Targeting attribute used by Segment rules and
decision traces. It does not select an artifact and does not replace `TargetRuntimeId`:

- Artifact selection uses `AsterloomReleaseClientOptions.TargetRuntimeId` only.
- Segment evaluation can use Context `platform`, `region`, `language`, `clientVersion`, or custom attributes.
- Use the same RID string in both fields to keep operational behavior understandable.

### 2.4 Package ID and Channel

- `PackageId` must equal Velopack `--packId` and remain stable for the lifetime of the product.
- Prefer one Asterloom Application per Package ID. The server manifest does not currently carry an independent
  Package ID, so never mix packages from different products in one Application/Channel.
- `stable`, `beta`, and `canary` are release channels, not operating-system platforms.
- Package with `--channel stable`, or explicitly select the channel through `UpdateOptions.ExplicitChannel`.

## 3. One-time platform setup

### 3.1 Create the scope

Use Web `/tenants` to create or select a Tenant, Application, and Environment, then retain their UUIDs. Keep
production separate from development and staging.

### 3.2 Configure Passport and authorization

Use a public OIDC client with Authorization Code + PKCE for a desktop application:

- Client ID such as `my-desktop-client`.
- Loopback redirect URI such as `http://localhost/`.
- At least the `asterloom.api` scope.
- No client secret embedded in the application.

The check endpoint requires `release.update.check`. Either bind a suitable role to a specific user/service client,
or create an application/environment-scoped `Any actor` Allow policy for `release.update.check` when all
authenticated users should be able to check.

There is currently no anonymous release feed. The safe implemented path checks after Passport sign-in. A product
that must update before sign-in needs a separate constrained bootstrap identity or anonymous signed-manifest
endpoint. Do not embed Client Credentials secrets in a desktop binary.

### 3.3 Register an external RSA public key

Generate an RSA key of at least 2048 bits. Keep the private key in CI, an HSM, or a signing service. Register only
the SubjectPublicKeyInfo public PEM at:

```text
Web → Releases → Artifacts → Signing trust store → Register public key
```

Embed the returned `fingerprint → publicKeyPem` trust mapping in the desktop client. Artifact and manifest
signatures use:

```text
algorithm: RSA-PSS-SHA256
input: UTF-8 bytes of the lowercase 64-character SHA-256 hex text
output: Base64 detached signature
```

```csharp
using System.Security.Cryptography;
using System.Text;

static string SignSha256Text(RSA privateKey, string sha256) =>
    Convert.ToBase64String(privateKey.SignData(
        Encoding.UTF8.GetBytes(sha256.ToLowerInvariant()),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pss));
```

OS code signing and Asterloom Release signing are independent protections. Production Windows and macOS packages
should use both.

### 3.4 Create channels

Use Web `/channels` to create immutable client-facing keys such as:

- `stable` for general availability;
- `beta` for opt-in early access;
- `canary` for internal or very small cohorts.

A Channel has one Active Release at a time and retains its Previous Release for channel rollback control.

## 4. Packaging contract

The repository currently pins Velopack `1.2.0`; pin `vpk` to the same version in CI:

```powershell
dotnet tool install --global vpk --version 1.2.0

dotnet publish .\MyDesktopApp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\publish\win-x64

vpk pack `
  --packId Kirayuuki.MyDesktopApp `
  --packVersion 1.4.0 `
  --packDir .\publish\win-x64 `
  --mainExe MyDesktopApp.exe `
  --channel stable `
  --outputDir .\releases\win-x64
```

Rules:

- Use SemVer such as `1.4.0` or `1.4.0-beta.1`, not a four-part version such as `1.4.0.0`.
- Keep `--packId` unchanged across releases.
- Make `--packVersion` exactly equal to the Asterloom Artifact and Desktop Release version.
- Make `--channel` equal to the Asterloom channel the installed client will query.
- Run `dotnet publish` and `vpk pack` separately for every RID.
- Begin with Full `.nupkg` artifacts; add Delta only after the full path is proven.
- Distribute Setup/Installer for the initial install; do not label it as a Velopack Full Artifact.

Velopack packaging reference: <https://docs.velopack.io/getting-started/csharp>

### 4.1 Producing and publishing a Delta package

Asterloom stores and delivers Delta packages but does not generate them. `vpk pack` creates a Delta only when its
output directory contains a previous release for the same Package ID, Channel, and RID. Preserve the complete
Velopack release output as a CI artifact, or restore it before packaging the next version; keeping only the new
publish directory is not sufficient.

For example, packaging `1.4.0` while the `1.3.0` release is available normally produces:

```text
Kirayuuki.MyDesktopApp-1.4.0-full.nupkg
Kirayuuki.MyDesktopApp-1.4.0-delta.nupkg  # reconstructs 1.4.0 from 1.3.0
```

Upload and attach both files to the same Asterloom Desktop Release:

| File | Artifact kind | Release version | Delta from | Runtime |
| --- | --- | --- | --- | --- |
| `*-1.4.0-full.nupkg` | Full | `1.4.0` | empty | `win-x64` |
| `*-1.4.0-delta.nupkg` | Delta | `1.4.0` | `1.3.0` | `win-x64` |

Every published release must retain one verified Full artifact for each runtime. A Delta is an optimization, never
the only recovery package. Repeat the build and upload pair independently for every RID.

The current Asterloom contract uses **direct, exact-source deltas**. A client on `1.3.0` may receive a
`1.3.0 → 1.4.0` Delta. A client on `1.2.0` receives the Full package unless the `1.4.0` release also contains a
separately built Delta whose `Delta From Version` is exactly `1.2.0`. Asterloom does not currently assemble a
multi-release `1.2.0 → 1.3.0 → 1.4.0` Delta chain.

Velopack packaging details: <https://docs.velopack.io/packaging/overview>

## 5. Web release workflow

### 5.1 Generate a quick-upload signing bundle

The private key remains in CI, an HSM, or an offline signing environment. **Never select a private key in Web or
upload it to Asterloom.** The repository script signs every Full/Delta package in a directory and produces the
`signing-metadata.json` consumed by quick upload:

```powershell
./Deploy/Scripts/New-VelopackSigningBundle.ps1 `
  -PackagePath .\releases\win-x64 `
  -PrivateKeyPath C:\secure\release-private-key.pem `
  -OutputPath .\releases\win-x64\signing-metadata.json
```

`-PackagePath` may also receive multiple explicit files or wildcard paths. The script accepts only
`*-full.nupkg` and `*-delta.nupkg`, signs each package's lowercase SHA-256 text with RSA-PSS-SHA256, and writes:

```json
{
  "schemaVersion": 1,
  "algorithm": "RSA-PSS-SHA256",
  "fingerprint": "64-character lowercase public-key SHA-256 fingerprint",
  "artifacts": {
    "MyApp-1.4.0-stable-full.nupkg": {
      "sha256": "package SHA-256",
      "signature": "Base64 detached signature"
    }
  }
}
```

The bundle contains no private key. Its fingerprint must match an active public key in the current tenant's
Signing trust store. Register that public key once and reuse it for subsequent releases.

### 5.2 Default: C# Velopack quick upload

Web `/artifacts` opens on `C# Velopack quick upload` by default:

1. Select one or more `*-full.nupkg` / `*-delta.nupkg` files under `Velopack packages`.
2. Select the generated `signing-metadata.json` under `Signing bundle`.
3. Review the inferred values. If a Delta has several possible sources, select the correct Full version in that row.
4. Choose `Upload and verify all`. Web uploads Full packages before dependent Delta packages and automatically
   creates the ticket, transfers bytes, and completes every item.

No manual entry is required for:

- Package ID, Semantic Version, `channel`, and `rid`, read from the root NuSpec;
- Full/Delta kind, derived from the file-name suffix;
- SHA-256 and signature matching by exact file name;
- registered Signing Key selection by public-key fingerprint;
- Delta From Version, inferred from selected or server-side older Verified Full packages with the same RID (the
  highest eligible version is selected by default);
- exact artifacts that already exist in Verified state, which are skipped.

One batch may contain several versions and RIDs, but all packages must use one Package ID and Channel. Velopack
does not store the exact Delta source in its NuSpec, so this is the one field Web must infer from the batch and
server inventory. Change the row selector when that inference is not the intended source. If no source is
available, add the older Full package to the batch or switch to advanced upload.

Quick mode is not merely a browser-side check. After transfer, the server opens the actual `.nupkg` in object
storage and checks its file name, root NuSpec, version, RID, and Full/Delta kind against the request before
combining those results with size, media type, SHA-256, and RSA-PSS signature verification. A modified browser
request therefore cannot bypass package-content validation.

### 5.3 Advanced upload (the retained original flow)

Choose `Advanced upload` for non-Velopack artifacts, an unusual Delta source, or troubleshooting:

1. Select the artifact file.
2. Enter Release Version, `targetRuntimeId`, Full/Delta, Delta From Version, and media type.
3. Wait for Web to calculate SHA-256.
4. Sign that SHA-256 text in the external signer.
5. Select the registered public key and paste the Base64 signature.
6. Create the short-lived upload ticket, then upload and verify.

Only a `Verified` artifact can be attached to a release. Common rejection causes include signing raw file bytes
or the binary digest instead of the lowercase digest text, using RSA PKCS#1 v1.5 instead of RSA-PSS, selecting the
wrong key, dropping required signed headers, or transferring content that differs from the ticket declaration.

Release artifacts use the tenant system bucket `release-artifacts`; do not create a normal bucket or bypass the
Release upload workflow for update packages.

### 5.4 Create, validate, and publish a draft

Open Web `/releases`, then configure Channel, Semantic Version, display name, notes, verified artifacts, minimum
version, rollout basis points, optional Target Segment, and Mandatory.

The rollout denominator is `100000`: `1000` = 1%, `5000` = 5%, `25000` = 25%, and `100000` = 100%.

After saving the draft:

1. Run `Validate release`.
2. Resolve every validation error.
3. Sign the Candidate Manifest SHA-256 with the external RSA key.
4. Select the Manifest Signing Key and paste the Base64 signature.
5. Publish the signed release.

Any draft change alters the manifest and requires another validation and signature. A published manifest is
immutable; create a new release for subsequent changes.

### 5.5 Simulate and roll out

Use the update simulator to cover current version, incompatible runtime, matching/non-matching segment, stable
keys inside and outside rollout, and clients below Minimum Version. A typical progression is:

```text
canary 100% → stable 1% → 5% → 25% → 50% → 100%
```

Observe startup failures, crashes, Telemetry errors, download failures, and business outcomes between promotions.

## 6. C# client integration

The SDK is currently provided as `net10.0` repository projects:

```powershell
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Identity\Asterloom.Sdk.Identity.csproj
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Rpc\Asterloom.Sdk.Rpc.csproj
dotnet add .\MyDesktopApp.csproj reference .\Backend\Asterloom.Sdk.Release\Asterloom.Sdk.Release.csproj
```

Run Velopack as early as possible in `Main`:

```csharp
using Velopack;

VelopackApp.Build().Run();
```

After Passport sign-in:

```csharp
using Asterloom.Sdk.Release;
using Asterloom.Sdk.Rpc;
using Velopack;

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);

var scope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
var trustedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [releaseKeyFingerprint] = releasePublicKeyPem,
};

using var releaseClient = new AsterloomReleaseClient(
    transport.HttpClient,
    new AsterloomReleaseClientOptions
    {
        Scope = scope,
        TargetRuntimeId = "win-x64",
        PackageId = "Kirayuuki.MyDesktopApp",
        TrustedPublicKeysByFingerprint = trustedKeys,
    });

var source = new AsterloomVelopackUpdateSource(
    releaseClient,
    currentVersion => AsterloomReleaseContext.Create(
        scope,
        targetingKey: installationId,
        clientVersion: currentVersion,
        platform: "win-x64"));

var manager = new UpdateManager(
    source,
    new UpdateOptions { ExplicitChannel = "stable" });

if (manager.IsInstalled)
{
    var update = await manager.CheckForUpdatesAsync();
    if (update is not null)
    {
        await manager.DownloadUpdatesAsync(update, ReportUpdateProgress, cancellationToken);
        await SaveApplicationStateAsync(cancellationToken);
        manager.ApplyUpdatesAndRestart(update);
    }
}
```

### 6.1 Delta selection and Full fallback

For an eligible client with an exact Delta, the update response keeps `selectedArtifact`/`download` for backward
compatibility and also returns `artifactDownloads` containing exactly:

1. the target-version Full package and its short-lived ticket;
2. the one Delta whose `DeltaFromVersion` equals the client's current version and its ticket.

`AsterloomVelopackUpdateSource` exposes both assets to `UpdateManager`. Velopack first attempts the Delta,
validates the reconstructed target package, and automatically downloads the Full package if download,
reconstruction, or validation fails. If no exact Delta exists, or the client is below Minimum Version, Asterloom
returns only Full. Download tickets are tracked and refreshed independently for each asset.

Applications using Velopack should keep calling `DownloadUpdatesAsync`; no custom fallback loop is required. A
non-Velopack client can inspect `decision.ArtifactDownloads` and call
`DownloadArtifactToAsync(decision, artifactId, destination)` for a specific signed asset. The original
`DownloadToAsync` method continues to download `SelectedArtifact` only.

Velopack download/fallback behavior: <https://docs.velopack.io/integrating/overview>

Generate `installationId` once and persist it. Recreating it on every launch changes deterministic rollout
membership. A stable User ID can be used for user-based rollout, but switching accounts then changes the result.

Before exposing an artifact, `AsterloomReleaseClient` verifies the trusted fingerprint, manifest signature,
manifest payload, artifact metadata, downloaded size and SHA-256, and detached artifact signature. Never bypass the
client and download the signed URL directly.

`Mandatory` is application policy metadata; it does not automatically lock the application UI. Call
`AsterloomReleaseClient.CheckForUpdateAsync` when the application needs to inspect `decision.Mandatory` and decide
whether dismiss, offline continuation, retries, or restart can be deferred.

## 7. Decision and control semantics

The server evaluates an update in this order:

1. Active Tenant, Application, and Environment.
2. Active Channel with an Active Release.
3. Release is not paused.
4. Target version is greater than current version.
5. Optional Target Segment matches.
6. Stable bucket is below Rollout Basis Points.
7. A Verified artifact exactly matches `targetRuntimeId`.
8. Below Minimum Version: select Full and mark Mandatory.
9. Otherwise prefer an exact-source Delta and fall back to Full.

Control behavior:

- Pause stops new update decisions but does not uninstall an already installed version.
- Promote increases rollout while stable targeting keys retain deterministic membership.
- Rollback repoints the Channel to an earlier signed release and prevents further upgrades to the bad release.
- Current Asterloom and default Velopack behavior move forward only; Rollback does **not** downgrade clients that
  already installed the newer release.

Forced downgrade requires a separate high-risk recovery design with explicit Velopack downgrade support and data
compatibility handling.

## 8. Key rotation

Old clients trust only embedded keys. Rotate safely:

1. Sign a transition release with the old key.
2. Embed both old and new public keys in that transition client.
3. Wait for the supported population to upgrade.
4. Begin signing new releases with the new private key.
5. Remove the old public key only after the support window.

Immediately switching to a new key strands old clients that cannot authenticate the update carrying that key.

## 9. Executable Reference App regression

The repository pins both the C# package and `vpk` to 1.2.0. Build two real Sample App versions with:

```powershell
./Deploy/Scripts/Build-Reference-DesktopUpdate.ps1 `
  -OutputDirectory "$env:TEMP/asterloom-reference-update"
```

The script retains the 1.0.0 installer, creates 1.0.0/1.1.0 Full packages and the direct Delta, reconstructs the
target Full with `vpk delta patch`, and requires a byte-identical SHA-256. Configure the three generated package
paths through `ASTERLOOM_REFERENCE_RELEASE_BASE_FULL`, `_TARGET_FULL`, and `_TARGET_DELTA`, then run Reference App
`provision` to upload, sign, and publish both versions.

Install the retained baseline Setup and run `Asterloom.ReferenceApp.Client.exe update RESULT.json`. The installed
client records which artifact types the Asterloom source actually downloaded, applies the update, restarts, and
records the Velopack restart version. Reinstall the baseline and add `--force-full` to prove the Full path. A test
passes only when the normal run downloads Delta without Full, the forced run downloads Full without Delta, and
both restart into the target assembly version. Running from `bin/` or a portable folder is intentionally rejected.

## 10. CI checklist

- [ ] Publish with the correct RID and Release configuration.
- [ ] Pin `vpk` to the application Velopack version.
- [ ] Restore/preserve the previous Velopack release output before building a Delta.
- [ ] Match Package ID, Channel, SemVer, Asterloom version, and runtime exactly.
- [ ] Apply OS code signing where required.
- [ ] Keep the private RSA key in a controlled signer.
- [ ] Generate the artifact digest signature.
- [ ] Confirm the artifact is Verified.
- [ ] Attach one Full per RID and label each Delta with its exact source version.
- [ ] Validate and externally sign the candidate manifest.
- [ ] Run simulation with fixed test installation IDs.
- [ ] Perform a real installed canary download, replace, and restart test.
- [ ] Test both successful Delta reconstruction and forced Full fallback.
- [ ] Prepare Telemetry/Analytics monitoring and a signed rollback target.

## 11. Related implementation

- Release client: [AsterloomReleaseClient.cs](../../Backend/Asterloom.Sdk.Release/AsterloomReleaseClient.cs)
- Velopack adapter: [AsterloomVelopackUpdateSource.cs](../../Backend/Asterloom.Sdk.Release/AsterloomVelopackUpdateSource.cs)
- Signature verification: [AsterloomReleaseVerifier.cs](../../Backend/Asterloom.Sdk.Release/AsterloomReleaseVerifier.cs)
- Runtime protocol: [release.proto](../../Proto/Asterloom/release/v1/release.proto)
- Admin protocol: [release_admin.proto](../../Proto/Asterloom/release/v1/release_admin.proto)
- Executable signing/upload example: [ReferenceAppProvisioner.cs](../../Backend/Samples/Asterloom.ReferenceApp.Client/ReferenceAppProvisioner.cs)
- Reproducible Full/Delta builder: [Build-Reference-DesktopUpdate.ps1](../../Deploy/Scripts/Build-Reference-DesktopUpdate.ps1)
- General feature guide: [Feature-Guide.md](../Feature-Guide.md)
- Velopack C# guide: <https://docs.velopack.io/getting-started/csharp>
- Velopack UpdateManager: <https://docs.velopack.io/reference/cs/Velopack/UpdateManager>
