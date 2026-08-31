# Operations: API catalog, OpenAPI, and health checks

[English](Operations.md) | [简体中文](Operations.zh-CN.md) | [Module index](README.md)

Operations is a read-only control plane for discovering the APIs exposed by the running instance, dependency health,
and the generated contract document. It supports operators and integrations; it does not replace domain APIs,
Telemetry backends, or deployment-platform probes.

## 1. Web management

| Page | Route | Capability |
| --- | --- | --- |
| API Catalog | `/operations/apis` | Search service/RPC/HTTP path, filter `admin`/`runtime`, inspect request and response types |
| Health | `/operations/health` | Inspect aggregate status, duration, dependencies, descriptions, and tags |

The API page can also download the current instance's OpenAPI 3 JSON and display its SHA-256. The document is a
runtime snapshot generated from Protobuf HTTP annotations and ASP.NET OpenAPI metadata. Use it for Kiota/OpenAPI
Generator and release-time contract comparisons.

## 2. Operations Admin API

| RPC | JSON Transcoding | Permission |
| --- | --- | --- |
| `ListApis` | `GET /api/v1/operations/apis` | `operations.api.read` |
| `GetOperationsHealth` | `GET /api/v1/operations/health` | `operations.health.read` |
| `GetOpenApiDocument` | `GET /api/v1/operations/openapi` | `operations.openapi.read` |

`ListApis` discovers Asterloom services from compiled Protobuf descriptors and returns service, RPC, category, HTTP
method/path, request and response type, and the deprecated flag. `query` is limited to 200 characters; `category`
must be empty, `admin`, or `runtime`.

The OpenAPI response contains `contentType`, JSON `content`, lowercase SHA-256, and `generatedAt`. It is generated
once and cached in each process. Retrieve it again after deploying a new Server; do not treat an unversioned runtime
response as the only retained contract artifact.

## 3. Kubernetes and load-balancer probes

The Server also exposes three lightweight HTTP health endpoints:

| Endpoint | Current checks | Purpose |
| --- | --- | --- |
| `/health/live` | `self` | Process is alive; restart when it fails |
| `/health/ready` | `self` plus `ready` dependencies, currently PostgreSQL | Instance may receive traffic |
| `/health/startup` | `self` plus `startup` dependencies, currently PostgreSQL | Cold start has completed |

These probes require no bearer token and are intended for orchestrators and load balancers. They return the compact
ASP.NET health status. Use the permission-protected Operations Health API for dependency details.

Kubernetes example:

```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8000 }
readinessProbe:
  httpGet: { path: /health/ready, port: 8000 }
startupProbe:
  httpGet: { path: /health/startup, port: 8000 }
  failureThreshold: 30
  periodSeconds: 2
```

Do not make liveness depend on a temporarily unavailable external Analytics or Telemetry backend. Choose readiness
and startup tags according to whether a dependency is truly required to serve requests.

## 4. Contract generation and coverage

After changing `.proto` files, run:

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

The sync script updates OpenAPI and the Web Kiota client. Coverage checks require:

- A `google.api.http` mapping for every custom RPC so JSON Transcoding can expose it.
- A permission mapping for every Admin RPC.
- A Web route, `data-ui-action`, and declared E2E coverage for every Admin RPC.

`/swagger` is available in Development. Production does not redirect the root to Swagger; integrations should use
the protected `GetOpenApiDocument` endpoint or retained generated artifacts.

## 5. Troubleshooting

1. `/health/live` fails: inspect process/container exit state and startup logs.
2. live succeeds but ready/startup fails: inspect dependencies in `/operations/health`, currently focusing on PostgreSQL.
3. Web reports Backend Unavailable: verify `ASTERLOOM_BACKEND_URL`, Nginx routing, Server container port `8000`, and Compose host port `60001`.
4. An API returns a structured error: retain `X-Request-ID` and correlate it through Audit, Telemetry, and Server logs.
5. An RPC is absent from the catalog: ensure its Proto descriptor is compiled and rerun protocol sync/coverage checks.
6. The OpenAPI hash differs: verify the Server version reached by the request and compare the JSON documents semantically.

Health descriptions must not contain connection strings, passwords, or internal tokens. Grant Operations permissions
minimally, especially `operations.openapi.read`, which reveals the complete contract.

## 6. Implementation references

- Admin protocol: [operations_admin.proto](../../Proto/Asterloom/operations/v1/operations_admin.proto)
- Types: [operations_types.proto](../../Proto/Asterloom/operations/v1/operations_types.proto)
- Metadata service: [OperationsMetadataService.cs](../../Backend/Asterloom.Module.Rpc/Operations/OperationsMetadataService.cs)
- Health endpoints: [Program.cs](../../Backend/Asterloom.Server/Program.cs)
- API coverage tool: [Asterloom.ApiCoverage](../../Backend/Tools/Asterloom.ApiCoverage)
- Web: [operations-workspace.tsx](../../Frontend/features/operations/operations-workspace.tsx)
