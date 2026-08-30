# RPC/HTTP: gRPC, JSON Transcoding, and OpenAPI

[English](Rpc-Http.md) | [简体中文](Rpc-Http.zh-CN.md) | [Module index](README.md)

Asterloom uses Protobuf as its only business contract while exposing native gRPC and HTTP/JSON. Browsers call
JSON Transcoding through the Next.js BFF; .NET applications prefer strongly typed native gRPC. Business logic is
implemented once.

## 1. Transport topology

```text
.NET client ── HTTP/2 gRPC ───────────────┐
                                          ├─ Asterloom.Server gRPC service
Browser ── Next.js BFF ── HTTP/JSON ──────┘      + shared auth/audit/error logic
```

Every custom RPC must declare `google.api.http` in its `.proto`. JSON routes, OpenAPI, and the Kiota client derive
from the same contract. Do not implement a second REST controller with different behavior.

## 2. Server baseline

```csharp
builder.Services.AddGrpc().AddJsonTranscoding();

app.MapGrpcService<MyService>();
```

`AddAsterloomRpc()` also configures:

- gRPC JSON Transcoding and gRPC Swagger.
- Bearer authentication, authorization, and audit interceptors.
- Structured errors and request IDs.
- Request decompression.
- A default 4 MiB gRPC send/receive limit.

Do not put large files in Protobuf; use [File Storage](File-Storage.md) signed transfer URLs.

## 3. Shared C# authenticated transport

```csharp
using Asterloom.Sdk.Rpc;

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);

var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
using var config = new AsterloomConfigClient(
    transport.HttpClient,
    configOptions);
```

- `CallInvoker` serves native gRPC SDKs.
- `HttpClient` serves JSON and transfer-aware SDKs.
- The bearer handler attaches tokens only to the same Asterloom scheme, host, and port.
- AWS SigV4 URLs receive no bearer token, avoiding dual authentication.
- Plain HTTP is limited to explicitly enabled loopback development; production requires HTTPS.

Reverse proxies must preserve HTTP/2 gRPC, long requests, and JSON Transcoding. Do not proxy native gRPC as
ordinary HTTP/1.1.

## 4. Protocol change workflow

1. Edit or add `Proto/Asterloom/<domain>/v1/*.proto`.
2. Add an HTTP mapping to every business RPC.
3. Implement the generated C# service in Backend.
4. Run:

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

The script builds a development server, exports `Docs/Protocol/openapi/asterloom-v1.json`, and regenerates
`Frontend/lib/api/generated` with Kiota. Coverage requires every Admin RPC to have a permission, UI route, UI
action marker, and E2E test.

## 5. Error contract

Domain errors map to standard gRPC statuses: InvalidArgument, NotFound, AlreadyExists, Aborted,
FailedPrecondition, Unauthenticated, and PermissionDenied. Rich status contains:

- `google.rpc.ErrorInfo` with stable reason/error code and request ID.
- `google.rpc.BadRequest` for field validation.
- `x-asterloom-error-code` and `x-request-id` trailers.
- `X-Request-ID` on HTTP responses for Audit and Telemetry correlation.

Unhandled exceptions return only generic `internal_error`; details remain in controlled logs and Telemetry.

## 6. API compatibility

- Packages and HTTP paths carry `v1`.
- Never reuse published field numbers or casually change field/enum semantics.
- Add fields compatibly with explicit defaults.
- Writes use `expectedVersion` for concurrency.
- Lists use `page_size/page_token/query`; callers do not depend on database offsets.
- Design long-running, streaming, or large-payload operations explicitly rather than forcing them into a Unary RPC.

## 7. Browser boundary

The browser does not hold an access token or create a gRPC client. It calls same-origin `/api/asterloom/*`; the
BFF reads server-side session tokens, calls JSON Transcoding, and returns a controlled result. See
[Web Console/BFF](Web-Console-Bff.md).

## 8. Verification

```powershell
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

Test both native gRPC and HTTP/JSON, especially enums, timestamps, 64-bit integers, error mapping, authentication,
and reverse-proxy behavior.

## 9. Related implementation

- RPC baseline: [RpcServiceCollectionExtensions.cs](../../Backend/Asterloom.Module.Rpc/RpcServiceCollectionExtensions.cs)
- Error mapping: [AsterloomExceptionInterceptor.cs](../../Backend/Asterloom.Module.Rpc/Errors/AsterloomExceptionInterceptor.cs)
- Shared transport: [AsterloomAuthenticatedTransport.cs](../../Backend/Asterloom.Sdk.Rpc/AsterloomAuthenticatedTransport.cs)
- Protocol sync script: [Sync-ProtocolArtifacts.ps1](../../Deploy/Scripts/Sync-ProtocolArtifacts.ps1)
- API coverage matrix: [admin-api-coverage.yaml](../Protocol/admin-api-coverage.yaml)
- Standard endpoints: [standard-endpoints.md](../Protocol/standard-endpoints.md)
