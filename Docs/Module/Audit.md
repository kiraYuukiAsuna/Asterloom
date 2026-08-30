# Audit: administrative operation history

[English](Audit.md) | [简体中文](Audit.zh-CN.md) | [Module index](README.md)

Audit answers who performed an administrative operation, when, against which resource, and with what outcome. It
covers sensitive control-plane operations. It is not product event collection or application logs/traces; see
[Analytics](Analytics.md) and [Telemetry](Telemetry.md) for those signals.

## 1. Recorded operations

The gRPC `AuditInterceptor` automatically records unary RPCs in `.admin.` services whose names begin with:

```text
Create / Update / Archive / Restore / Set / Remove / Delete / Import
Publish / Rollback / Rotate / Revoke / Export / Invite / Resend
Suspend / Reactivate / Complete / Copy / Pause / Promote
```

Tenant, user, role, Feature, Config, Storage, Release, and similar management changes therefore enter the audit log.
Ordinary List/Get reads are not recorded; exporting the audit log is recorded. Runtime APIs, Analytics events, and
OpenTelemetry signals are not Audit Events.

Each event contains:

| Field | Meaning |
| --- | --- |
| `actorId` | The token `sub`, or `unknown` when absent |
| `tenantId/applicationId/environmentId` | Resource scope found in the request or response |
| `operation` | Full gRPC method, such as `/asterloom.feature.admin.v1.FeatureAdminService/PublishFlag` |
| `resourceType/resourceId` | Normalized resource type and target identifier |
| `requestId` | ASP.NET trace identifier used to correlate logs, traces, and error responses |
| `outcome` | `Succeeded`, `Denied`, or `Failed` |
| `errorCode` | Asterloom error code or gRPC status on failure |
| `changeSummary` | Request field names, not field values |
| `createdAt` | UTC creation time |

The interceptor deliberately does not serialize request values, so passwords, secrets, tokens, and payloads are not
copied into `changeSummary`. Callers must still avoid placing sensitive values in resource keys, IDs, or other
operation metadata.

## 2. Web management

Route: `/audit`

The Web Console can:

- Filter by actor ID, operation, outcome, request ID, and time range.
- Page through results and inspect an individual event.
- Export CSV with the same filters.
- Copy a request ID from an error or Telemetry view and pivot back to the administrative operation.

List calls default to 50 rows and allow at most 100 per page. CSV defaults to 1,000 rows and accepts 1–10,000; the
file is returned directly in the API response, so narrow the filters for large periods.

## 3. API and permissions

| RPC | JSON Transcoding | Permission |
| --- | --- | --- |
| `ListAuditEvents` | `GET /api/v1/audit/events` | `audit.event.read` |
| `GetAuditEvent` | `GET /api/v1/audit/events/{auditEventId}` | `audit.event.read` |
| `ExportAuditEvents` | `POST /api/v1/audit/events:export` | `audit.event.export` |

Audit has no dedicated application SDK. Administrators can use the Web Console, generated gRPC client, or JSON API.
Applications must not use Admin APIs to insert custom audit events; use Analytics for product events and Telemetry
for technical signals.

JSON query example:

```http
GET /api/v1/audit/events?actorId=USER_ID&outcome=AUDIT_OUTCOME_FAILED&pageSize=50
Authorization: Bearer <admin-access-token>
```

Export body:

```json
{
  "requestId": "REQUEST_ID",
  "maximumRows": 1000
}
```

## 4. Outcome and failure semantics

- A completed RPC records `Succeeded`; fine-grained authorization rejection that reaches the gRPC interceptor records
  `Denied`; other exceptions record `Failed`.
- A missing or invalid bearer token may be rejected earlier by ASP.NET endpoint authorization, before the Audit
  Interceptor runs. Do not assume every anonymous probe creates an Audit Event; retain Nginx/Server access logs too.
- Audit persistence uses an independent `CancellationToken.None` to attempt the append before request completion.
- If the audit store fails, the current implementation emits a Critical log but does not change the original Admin
  RPC result. Production alerts must watch `AuditInterceptor` Critical logs; a successful mutation does not prove its
  audit append succeeded.
- There is no Admin API to edit or delete Audit Events, and no built-in online archive screen. Retention, database
  partitioning, backup, and compliance export remain operational policies.

## 5. Security and operations checklist

- Reserve `audit.event.read` and especially `audit.event.export` for security or audit staff.
- CSV still includes actor, resource, and correlation data; encrypt it, restrict sharing, and destroy it under policy.
- Alert on spikes in Denied/Failed, sensitive permission changes, client-secret rotations, and large exports.
- Preserve one `X-Request-ID` through Nginx, BFF, Server, and Telemetry for end-to-end correlation.
- Periodically verify that critical Admin RPCs appear and that database backups restore audit records.

## 6. Implementation references

- Admin protocol: [audit_admin.proto](../../Proto/Asterloom/audit/v1/audit_admin.proto)
- Types: [audit_types.proto](../../Proto/Asterloom/audit/v1/audit_types.proto)
- Interceptor: [AuditInterceptor.cs](../../Backend/Asterloom.Module.Rpc/Auditing/AuditInterceptor.cs)
- Management service: [AuditManagementService.cs](../../Backend/Asterloom.Module/Auditing/AuditManagementService.cs)
- PostgreSQL store: [PostgreSqlAuditStore.cs](../../Backend/Asterloom.Module.Infrastructure/Auditing/PostgreSqlAuditStore.cs)
- Web: [audit-workspace.tsx](../../Frontend/features/audit/audit-workspace.tsx)
