# Asterloom File Storage Guide

[English](File-Storage.md) | [简体中文](File-Storage.zh-CN.md) | [Module index](README.md)

This guide describes the file-storage model, Web administration entry points, application integration,
transfer protocol, permissions, and lifecycle in the current Asterloom C# implementation. File Storage is an
Asterloom-managed, S3-compatible object-storage capability. It is neither a PostgreSQL file column nor a way to
give application code an administrative MinIO/S3 credential.

## 1. Resource model

```text
Tenant
  └─ Asterloom logical Bucket
       ├─ quota, per-object size, content-type, and access policy
       └─ Object metadata (PostgreSQL)
            └─ Object bytes (S3 / MinIO physical bucket)
```

- An Asterloom `Bucket` is a logical policy boundary inside a tenant. It does not require a separate, same-named
  physical S3 bucket.
- The current S3 transport uses one physical bucket named `asterloom-objects` by default and isolates tenants,
  logical buckets, and objects with internal physical keys.
- PostgreSQL stores buckets, objects, upload sessions, status, versions, SHA-256 digests, and custom metadata.
  S3-compatible storage holds the bytes.
- A bucket belongs to a `Tenant`. An object can optionally carry `ApplicationId` and `EnvironmentId` ownership
  tags. Supplying an environment also requires its application.
- `ObjectKey` is a unique business key inside a bucket, such as `avatars/user-42.png`; it is not a local path.

Use File Storage for attachments, exports, images, models, and other large opaque payloads. Data that needs field
queries, relational transactions, or frequent partial updates belongs in PostgreSQL. Passwords, tokens, and
private keys belong in a secret manager.

## 2. Where to create a bucket in the Web console

The Storage sidebar currently opens this route by default:

```text
/storage/objects
```

That page focuses on objects, which can make a new installation look read-only. The Storage workspace has two
tabs at the top:

| Page | Route | Capability |
| --- | --- | --- |
| Buckets | `/storage/buckets` | List/Get/Create/Update/Archive/Restore buckets |
| Objects | `/storage/objects` | List/Get/Upload/Download/Metadata/Copy/Delete objects |

To create a bucket:

1. Open Storage from the sidebar.
2. Select **Buckets** at the top of the workspace.
3. Fill out the **Create bucket** card with the key, display name, quota, maximum object size, allowed content
   types, and access policy.
4. Return to **Objects**, select the bucket, and upload or manage objects manually.

The Web console therefore covers every current Storage Admin API. The create entry point is simply not a
separate sidebar item.

## 3. Who creates buckets and who uploads files

Use a control-plane/data-plane split:

| Actor | Recommended responsibility |
| --- | --- |
| Platform administrator / operations | Pre-create buckets in the Web console and set quota, size, media-type, access, and authorization policy |
| Application backend / trusted client | Use a configured `BucketId` and the Storage SDK/API to upload and download business files |
| Web console | Troubleshooting, manual transfer, metadata inspection, copying, and deletion |

A normal business application should not need to create buckets dynamically. Inject its `BucketId` as
configuration and grant only the required `storage.object.upload`, `storage.object.download`, and related
permissions.

If a workload genuinely needs a bucket per project, grant its service identity `storage.bucket.create` and call
the gRPC/JSON Transcoding API. The current `AsterloomStorageClient` concentrates on object transfer and does not
wrap bucket administration; use a generated API client or the Storage Admin API for that control-plane work.

## 4. Bucket settings

| Field | Meaning |
| --- | --- |
| Key | Stable, tenant-unique identifier using lowercase letters, digits, `.`, `_`, and `-` |
| Display Name / Description | Human-readable administration information |
| Quota | Maximum total of used and upload-reserved bytes |
| Maximum Object | Per-object byte limit; it cannot exceed the quota |
| Allowed Content Types | Empty or `*/*` allows all; media wildcards such as `image/*` are supported |
| Access Policy | `Private` or `AuthenticatedRead` |

In the current implementation, `AccessPolicy` is persisted and returned, but the download RPC still always
passes through the authorization interceptor and requires `storage.object.download`. `AuthenticatedRead` does
not currently produce a permission-free public URL or bypass Casbin authorization. Treat it as reserved policy
metadata for a future end-user read path, not as “any signed-in Internet user can read this bucket.”

If quota is omitted, the server defaults to 10 GiB. If maximum object size is omitted, it defaults to the lesser
of 2 GiB and the quota. The Web form submits both values explicitly.

## 5. Upload is a three-phase protocol

With the production S3 transport, the protocol sends bytes directly to object storage and keeps large files out
of the Asterloom Server and Next.js BFF data path. The in-memory development transport's local transfer endpoint
still passes through Server/BFF:

```text
Application
  ├─ 1. Bearer token → CreateUploadSession (Asterloom API)
  │       └─ short-lived transfer URL, HTTP method, and required headers
  ├─ 2. File bytes → Transfer URL (S3 / MinIO)
  └─ 3. Bearer token → CompleteUpload (Asterloom API)
          └─ server validates size, content type, and SHA-256, then marks Available
```

### 5.1 Create an upload session

Calculate the complete file's byte size and SHA-256 first, then submit:

- `BucketId`
- `ObjectKey`
- `FileName`
- `ContentType`
- `SizeBytes`
- a 64-character hexadecimal `Sha256`
- optional `ApplicationId`, `EnvironmentId`, and `CustomMetadata`

The server checks bucket state, key uniqueness, content type, object-size limit, and remaining quota, then
reserves space. An upload session currently remains valid for 15 minutes.

### 5.2 Transfer the bytes

Send the file to the ticket's `Url` using its returned `Method` and every `RequiredHeader`. The URL can belong to
S3/MinIO rather than the Asterloom API origin:

- Do not attach an Asterloom bearer token.
- Do not assume the method is always `PUT`; use the ticket value.
- Do not change signed headers, content type, or request path.
- Permit only HTTPS transfer URLs in production.

### 5.3 Complete the upload

A successful byte transfer does not yet make the object available. Call Complete so the server can inspect the
physical object and compare its size, content type, and SHA-256. Only a complete match changes the state from
`Pending` to `Available`; otherwise, the invalid bytes are removed and the upload becomes Failed or Expired.

## 6. Upload and download with the C# SDK

Acquire a Passport token and create the shared authenticated transport first. `transport.HttpClient` calls only
the Asterloom API; internally, the SDK uses a transfer client that does not attach the bearer token to a signed
object-store URL.

```csharp
using System.Security.Cryptography;
using Asterloom.Sdk.Storage;

var path = "report.pdf";
var file = new FileInfo(path);

string sha256;
await using (var hashingStream = File.OpenRead(path))
{
    sha256 = Convert.ToHexStringLower(
        await SHA256.HashDataAsync(hashingStream, cancellationToken));
}

using var storage = new AsterloomStorageClient(
    transport.HttpClient,
    new AsterloomStorageClientOptions
    {
        Scope = new AsterloomStorageScope(tenantId),
        // Use true only for a local HTTP development object store.
        AllowInsecureTransferUrls = false,
    });

await using var source = File.OpenRead(path);
var stored = await storage.UploadAsync(
    new AsterloomStorageUploadRequest(
        BucketId: documentsBucketId,
        ObjectKey: "reports/monthly-report.pdf",
        FileName: file.Name,
        ContentType: "application/pdf",
        SizeBytes: file.Length,
        Sha256: sha256,
        ApplicationId: applicationId,
        EnvironmentId: environmentId,
        CustomMetadata: new Dictionary<string, string>
        {
            ["document-type"] = "monthly-report",
        }),
    source,
    cancellationToken);

await using var destination = File.Create("downloaded-report.pdf");
await storage.DownloadToAsync(
    stored,
    destination,
    ticketLifetime: TimeSpan.FromMinutes(5),
    cancellationToken: cancellationToken);
```

`UploadAsync` calls these operations in order:

1. `CreateUploadSessionAsync`
2. `UploadContentAsync`
3. `CompleteUploadAsync`

`DownloadToAsync` requests a short-lived download ticket, downloads directly, then verifies the actual byte
count and SHA-256. A download ticket can last from 30 seconds to 15 minutes; the SDK default is 5 minutes.

Set `AllowInsecureTransferUrls = true` only for an HTTP MinIO instance on a development machine. Do not enable
it in production.

## 7. Current C# SDK and API coverage

`AsterloomStorageClient` currently exposes these convenience methods:

- `CreateUploadSessionAsync`
- `UploadContentAsync`
- `CompleteUploadAsync`
- `UploadAsync`
- `DownloadToAsync`

The following capabilities already exist in gRPC/JSON Transcoding and the Web console but are not yet wrapped by
that runtime SDK:

- Bucket List/Get/Create/Update/Archive/Restore
- Object List/Get
- Object Metadata Update
- Object Copy/Delete
- Standalone download URL creation

This is an SDK-surface choice, not a missing backend or Web implementation. An application backend that needs
those operations can generate a client from `storage_admin.proto` or call its HTTP mappings. It must not edit the
PostgreSQL tables directly or bypass Asterloom to manipulate internal physical keys.

## 8. Permissions

| Operation | Permission |
| --- | --- |
| Read buckets | `storage.bucket.read` |
| Create a bucket | `storage.bucket.create` |
| Update a bucket | `storage.bucket.update` |
| Archive / restore a bucket | `storage.bucket.archive` / `storage.bucket.restore` |
| Read object metadata | `storage.object.read` |
| Update metadata | `storage.object.metadata.update` |
| Create and complete uploads | `storage.object.upload` |
| Create download tickets | `storage.object.download` |
| Copy objects | `storage.object.copy` |
| Delete objects | `storage.object.delete` |

The transfer URL uses its own short-lived signature rather than a bearer permission; authorization happens when
the ticket is issued. Anyone who obtains an unexpired ticket might be able to use it, so never place ticket URLs
in logs, Analytics events, or Telemetry attributes.

## 9. Object lifecycle and concurrency

```text
Pending ── Complete + integrity verified ──> Available ── Delete ──> Deleted
   └──────── expired / mismatch ──────────> Failed
```

- Object creation, metadata updates, and deletion use version-based optimistic concurrency.
- Duplicate `ObjectKey` values are not allowed in one bucket. Deleted metadata is retained and the unique
  constraint remains in force, so the current version does not permit key reuse after deletion. Use a new,
  versioned key for replacement content.
- Delete removes physical bytes and retains deleted metadata/audit evidence. There is currently no object restore
  API.
- A bucket can be archived only when it has no pending or available objects, upload reservations, or object
  count.
- An archived bucket rejects uploads until restored.

## 10. Relationship to Desktop Release

Desktop Release `.nupkg` files use the same underlying object-storage transport, but the Release module manages a
system logical bucket named `release-artifacts` and adds artifact state, external signatures, manifests, channels,
rollout, and update decisions.

Consequently:

- Use Storage Web/SDK for ordinary business files.
- Use the Release artifact upload workflow for desktop update packages.
- Uploading a `.nupkg` to a normal Storage bucket does not turn it into a Release artifact.

See the [Asterloom Desktop Update Guide](Desktop-Updates.md) for the complete desktop release flow.

## 11. Production checklist

- [ ] An administrator has created a dedicated logical bucket and recorded its stable `BucketId`.
- [ ] Quota, maximum object size, and allowed content types match the business boundary.
- [ ] The application identity has only required object permissions and no bucket administration by default.
- [ ] The application calculates size and SHA-256 before upload and calls Complete after transfer.
- [ ] Transfer URLs carry no Asterloom bearer token and are never logged.
- [ ] Production `Storage__PublicEndpoint` is reachable by clients over HTTPS.
- [ ] S3/MinIO credentials exist only on Asterloom Server and are never distributed to applications.
- [ ] Error handling covers large-file timeouts, expired tickets, quota exhaustion, hash mismatch, and duplicate
  keys.
- [ ] Business deletion, retention, and backup policy is defined.

## 12. Related implementation

- C# Storage SDK: [AsterloomStorageClient.cs](../../Backend/Asterloom.Sdk.Storage/AsterloomStorageClient.cs)
- SDK models: [AsterloomStorageModels.cs](../../Backend/Asterloom.Sdk.Storage/AsterloomStorageModels.cs)
- Storage administration protocol: [storage_admin.proto](../../Proto/Asterloom/storage/v1/storage_admin.proto)
- Storage type protocol: [storage_types.proto](../../Proto/Asterloom/storage/v1/storage_types.proto)
- Server business rules: [StorageManagementService.cs](../../Backend/Asterloom.Module.Storage/StorageManagementService.cs)
- S3 transport: [S3ObjectStorageTransport.cs](../../Backend/Asterloom.Module.Infrastructure/Storage/S3ObjectStorageTransport.cs)
- Web Storage workspace: [storage-workspace.tsx](../../Frontend/features/storage/storage-workspace.tsx)
- General feature guide: [Feature-Guide.md](../Feature-Guide.md)
