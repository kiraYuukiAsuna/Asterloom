# Asterloom Module Guides

[English](README.md) | [简体中文](README.zh-CN.md)

This directory documents Asterloom by user-facing capability. Every guide follows the current .NET/C# backend,
C# SDKs, Next.js Web console, and Protobuf contracts in this repository. Rust, Go, and C++ are outside the current
implementation scope.

## Recommended reading order

For a new application integration:

1. Read [Platform](Platform.md) and create the Tenant, Application, and Environment scope.
2. Read [Identity](Identity.md), register a Passport client, and choose user or service authentication. Products
   with their own registration/sign-in UI should also follow [Business application identity integration](Identity-Business-Integration.md).
3. Read [Authorization](Authorization.md), define business permissions, and compose RBAC, ACL, and ABAC.
4. Add Feature, Config, Release, Analytics, Telemetry, or Storage capabilities as required.
5. Read [RPC/HTTP](Rpc-Http.md) and [Web Console/BFF](Web-Console-Bff.md) for transport and browser boundaries.
6. Complete the deployment baseline with [Persistence](Persistence.md), [Audit](Audit.md), and
   [Operations](Operations.md).

## Module catalog

| Capability | Guide | Web entry | C# integration |
| --- | --- | --- | --- |
| Platform | [Resource scopes](Platform.md) | `/tenants` | gRPC/JSON Admin API |
| Identity | [Passport and accounts](Identity.md) | `/identity/users` | `Asterloom.Sdk.Identity` |
| Business account integration | [Public Client sign-in, business API validation, registration, and membership](Identity-Business-Integration.md) | Identity + Authorization workspaces | `Asterloom.Sdk.Identity` + `Asterloom.Sdk.Identity.AspNetCore` |
| Authorization | [RBAC, ACL, and ABAC](Authorization.md) | `/authorization/roles` | `Asterloom.Sdk.Authorization` |
| Targeting / Rollout | [Segments and deterministic rollout](Targeting-Rollout.md) | `/targeting/segments` | `Asterloom.Sdk.Targeting` |
| Feature Flag | [Feature flags](Feature-Flags.md) | `/features` | `Asterloom.Sdk.Feature` + OpenFeature |
| Dynamic Config | [Dynamic configuration](Dynamic-Config.md) | `/config` | `Asterloom.Sdk.Config` |
| Desktop Update | [Desktop updates](Desktop-Updates.md) | `/channels`, `/artifacts`, `/releases` | `Asterloom.Sdk.Release` + Velopack |
| Analytics | [Product analytics](Analytics.md) | `/analytics/schemas`, `/analytics/explorer` | `Asterloom.Sdk.Analytics` |
| Telemetry | [Technical observability](Telemetry.md) | `/telemetry/sources`, `/telemetry/health` | `Asterloom.Sdk.Telemetry` + OpenTelemetry |
| Mail | [Transactional application email](Mail.md) | `/mail/accounts`, `/mail/deliveries` | `Asterloom.Sdk.Mail` + MailKit |
| RPC / HTTP | [Unified transport and contracts](Rpc-Http.md) | `/operations/apis` | `Asterloom.Sdk.Rpc` |
| File Storage | [File and object storage](File-Storage.md) | `/storage/buckets`, `/storage/objects` | `Asterloom.Sdk.Storage` |
| Persistence | [PostgreSQL and migrations](Persistence.md) | `/operations/health` | Npgsql / module stores |
| Audit | [Administrative audit](Audit.md) | `/audit` | Audit Admin API |
| Operations | [API catalog and health](Operations.md) | `/operations/apis`, `/operations/health` | Operations Admin API |
| Web Console / BFF | [Browser administration](Web-Console-Bff.md) | all administration routes | Next.js BFF + Redis sessions |

## Shared conventions

- `Proto/Asterloom` is the only API contract source; native gRPC and JSON Transcoding share one implementation.
- Administrative writes require a bearer identity, the relevant permission, Web CSRF protection, and an
  optimistic-concurrency version.
- `Tenant → Application → Environment` is the principal business scope unless a guide states otherwise.
- The Web console must cover every Admin RPC; `Docs/Protocol/admin-api-coverage.yaml` verifies the mapping.
- SDK examples assume Identity has already acquired a token and created a shared
  `AsterloomAuthenticatedTransport`.
- Guides describe implemented behavior. Reserved fields without active semantics are called out explicitly.

## Overview documents

- [Architecture and implementation baseline](../Architecture.md)
- [Feature usage guide](../Feature-Guide.md)
- [Full-capability reference application](../Reference-Application.md)
- [Standard protocol endpoints](../Protocol/standard-endpoints.md)
