# Platform: Tenants, Applications, and Environments

[English](Platform.md) | [简体中文](Platform.zh-CN.md) | [Module index](README.md)

Platform is the shared business scope and resource-lifecycle kernel for every Asterloom capability. It does not
mean Windows, macOS, or Linux; Desktop Release represents an operating-system target with an artifact
`targetRuntimeId`.

## 1. Resource hierarchy

```text
Tenant
  ├─ Tenant Membership
  └─ Application
       └─ Environment
            ├─ Targeting / Feature / Config / Release
            ├─ Analytics / Telemetry
            └─ other environment-scoped resources
```

| Resource | Purpose | Stable fields |
| --- | --- | --- |
| Tenant | Organization, customer, or security boundary | `id`, immutable `slug` |
| Application | One product or service | `id`, immutable `slug` |
| Environment | Development/staging/production isolation | `id`, `slug`, `environmentType` |
| Membership | Whether an actor can see and enter a tenant | `tenantId + actorId` |

Do not use Environment for an operating system or create one Application per `win-x64` build. A desktop product
normally has one Application and attaches multiple RID artifacts to a release.

## 2. Web workflow

Route: `/tenants`

1. Create a Tenant and choose its immutable slug.
2. Create an Application inside the Tenant.
3. Create development, staging, and production Environments.
4. Mark production as `isProtected` to prevent accidental archival.
5. Add or remove actors through Tenant Membership.
6. Use the resulting UUIDs in the global scope selector and application configuration.

The Web console covers List/Create/Update/Archive/Restore for all three resources and List/Set/Remove for
memberships. Archive retains history; it is not physical deletion.

After creating an Application, the console can open the application initialization dialog. The default preset creates
a protected `production` Environment, OIDC clients/scopes, an application access Permission and runtime Allow policies,
starter Feature/Config/Storage/Release/Analytics/Telemetry resources, and one-time client secrets or write keys when
needed. Generated business permission keys are prefixed with `app.` before the tenant/application slug, for example
`app.acme-payments-checkout.access`; this keeps applications whose slugs start with reserved module names such as
`analytics-` from colliding with Asterloom's system permission namespaces.

## 3. Lifecycle and concurrency

- Slugs are immutable after creation; display names are editable.
- Update, Archive, Restore, and membership changes use `expectedVersion` optimistic concurrency.
- A protected Environment must be unprotected before archival.
- Runtime reads require an Active Tenant, Application, and Environment. Child data remains durable when a parent
  is archived but is unavailable at runtime.
- Restore parents before restoring or using child resources.

## 4. API and integration

There is no dedicated Platform runtime SDK. Administrative code can generate a gRPC client from
[`platform_admin.proto`](../../Proto/Asterloom/platform/v1/platform_admin.proto) or call its JSON Transcoding
routes. Runtime applications normally retain the three IDs and pass them to capability SDKs:

```csharp
var featureScope = new AsterloomFeatureScope(tenantId, applicationId, environmentId);
var configScope = new AsterloomConfigScope(tenantId, applicationId, environmentId);
var releaseScope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
```

Inject scope IDs through deployment configuration. Do not rediscover them by display name at startup.

## 5. Permissions

- `platform.info.read`
- `platform.tenant.read/create/update/archive/restore`
- `platform.application.read/create/update/archive/restore`
- `platform.environment.read/create/update/archive/restore`
- `platform.tenant.membership.read/set/remove`

Membership controls Tenant visibility; Authorization roles and policies control allowed actions. Neither replaces
the other.

## 6. Implementation guidance

- Use separate Environments for development, staging, and production rather than simulating isolation with flags.
- Keep one stable Application per product unless security, lifecycle, or ownership is genuinely independent.
- Protect production and tightly restrict environment update/archive permissions.
- Use UUIDs or slugs in CI, configuration, and audit correlation; display names are presentation only.
- Review Release, Config, Feature, and Storage dependencies before archival.

## 7. Related implementation

- Protocol: [platform_admin.proto](../../Proto/Asterloom/platform/v1/platform_admin.proto)
- Business rules: [PlatformManagementService.cs](../../Backend/Asterloom.Module/Platform/PlatformManagementService.cs)
- Models: [PlatformResources.cs](../../Backend/Asterloom.Module/Platform/Model/PlatformResources.cs)
- Web: [platform-workspace.tsx](../../Frontend/features/platform/platform-workspace.tsx)
- Architecture: [Architecture.md](../Architecture.md)
