# Authorization: RBAC, ACL, and ABAC

[English](Authorization.md) | [简体中文](Authorization.zh-CN.md) | [Module index](README.md)

Authorization decides whether an actor already authenticated by Identity may perform an action on a business
resource inside a `Tenant → Application → Environment` scope.

Asterloom supports three complementary models. ABAC is not another name for ACL:

| Model | Question | Asterloom representation | Example |
| --- | --- | --- | --- |
| RBAC | What responsibility does the user have? | Permission → Role → Role Binding | `finance-operator` owns `orders.refund` |
| ACL | Which concrete resource may they access? | Policy `resourceType` + `resourceId` | only `order/order-42` |
| ABAC | Do trusted subject, resource, and request facts satisfy a rule? | typed Policy `condition` | finance department and amount below 5000 |

ACL and ABAC attach to the same Policy Rule and can be used separately or with RBAC. Casbin.NET enforces scope,
permission, resource, and Allow/Deny semantics. ABAC reuses Asterloom Targeting's typed evaluator.

## 1. Complete decision model

```text
authenticated actor + token-bound application scope
  ├─ RBAC: Role Binding → application Role → Permission
  └─ Policy Rule
       ├─ subject: Actor / Role / Any authenticated actor
       ├─ permission
       ├─ scope: Tenant / Application / Environment
       ├─ ACL: resourceType / resourceId
       ├─ ABAC: subject.* / resource.* / context.* / scope.*
       └─ effect: Allow / Deny
                ↓
       active permission + matching scope + matching resource + matching condition
                ↓
       any Deny wins; otherwise any Allow wins; otherwise default Deny
```

The result contains `allowed`, a readable `reason`, matched Policy IDs, and matched Role keys. A runtime decision
uses the current policy snapshot; archiving a permission, role, binding, or policy affects the next remote check.

## 2. Application permission catalog

System Permissions such as `platform.*`, `feature.*`, and `storage.*` describe Asterloom APIs. They are immutable.
Each business Application can define its own permissions, for example:

- `orders.read`
- `orders.refund`
- `invoice.approve`
- `project.member.invite`

An application key must be lowercase, contain a separator, and must not use an Asterloom-owned module namespace.
It belongs to the selected Tenant/Application. Different Applications may use the same key, but roles and policies
cannot reference another Application's definition.

Prefer an application-owned namespace such as `app.orders.refund` or `app.acme-payments.access` for generated
business permissions. Avoid deriving the first segment directly from Tenant or Application slugs, because a slug such
as `analytics-payments` would otherwise collide with the reserved `analytics.*` system namespace.

Archiving a Permission immediately makes it inactive even if an active Role still contains the key. Restoring it
reactivates existing roles and policies without recreating them. Revisions remain durable.

## 3. RBAC

A custom business Role must belong to one Tenant/Application; global custom Roles are rejected. It may contain
System Permissions and active permissions from that same Application. A Role Binding assigns it to an actor and
may narrow the grant to an Environment.

```text
Role owner scope: tenant A / application Orders
Binding scope:    tenant A / application Orders / environment Production
Request scope:    tenant A / application Orders / environment Production  → may match
Request scope:    tenant A / application CRM                              → never matches
```

System Roles (`super-administrator`, `tenant-administrator`, `operator`, `developer`, and `viewer`) manage the
Asterloom control plane. Use Application Roles for business users instead of treating platform administrators as
business roles.

## 4. ACL

A Policy Rule has an optional resource selector:

- `resourceType`: normalized lowercase type such as `order`, `document`, or `project`.
- `resourceId`: a stable business ID such as `order-42`; it requires a resource type.

An empty value means unrestricted. Current ACL matching is **exact**; `*`, paths, and regular expressions are not
interpreted as wildcards. Set only `resourceType` to cover all resources of that type. Create one rule per concrete
resource, or express ownership/organization rules with ABAC.

```json
{
  "effect": "POLICY_EFFECT_ALLOW",
  "subjectType": "POLICY_SUBJECT_TYPE_ACTOR",
  "subject": "8e07669e-7d6e-4abc-9dd7-fd503d6dced2",
  "scope": { "tenantId": "...", "applicationId": "..." },
  "permission": "orders.refund",
  "resourceType": "order",
  "resourceId": "order-42"
}
```

## 5. ABAC

An ABAC condition supports Targeting's `ALL`/`ANY` match modes and typed text, truth, and numeric operators:
equality, sets, contains, prefixes/suffixes, numeric comparisons, existence, and semantic-version comparisons.
Attribute names must use these namespaces:

| Prefix | Trusted source | Example |
| --- | --- | --- |
| `subject.*` | business user profile or organization data | `subject.department=finance` |
| `resource.*` | authoritative resource data from the business database | `resource.amount=1200` |
| `context.*` | server-verified facts for this request | `context.mfa=true` |
| `scope.*` | populated by Asterloom from the request | `scope.applicationId=...` |

Asterloom always overwrites and supplies `subject.id`, `resource.type`, `resource.id`, `scope.tenantId`,
`scope.applicationId`, and `scope.environmentId`, so callers cannot forge those authoritative values.

The critical trust boundary is enforced at runtime: a Public Client/User Token may check only itself and **cannot
submit custom ABAC attributes**. Only an application-bound Confidential Client may submit attributes on behalf of
an active Application member. The business backend must construct them from its database, the validated token, or
a trusted risk engine. Never forward browser-provided `department`, `ownerId`, or `amount` as authorization facts.

## 6. Recommended business flow

```text
Desktop / mobile Public Client
  1. Passport Authorization Code + S256 PKCE
  2. user Access Token → business API
                           ↓
Business backend
  3. validate JWT issuer / audience / tenant / application / user actor type
  4. load trusted user, resource, and organization attributes from its own DB
  5. call Asterloom CheckAccess using its Confidential Client service token
     actorId = user token sub
  6. execute only when decision.allowed=true
```

The User Access Token proves who calls the business API. The Service Token proves which trusted backend is
submitting authorization context. Both identities are involved; the service token does not replace user context.

## 7. C# integration

### 7.1 Current-actor RBAC/ACL pre-check

Useful for UX or decisions that need no business attributes. It never replaces backend enforcement:

```csharp
using Asterloom.Sdk.Authorization;

var authorization = new AsterloomAuthorizationClient(userTransport.CallInvoker);
var decision = await authorization.CheckAccessAsync(
    actorId: null, // inferred from the current User Token sub
    permission: "orders.read",
    scope: new AsterloomAuthorizationScope(tenantId, applicationId),
    resourceType: "order",
    resourceId: orderId,
    attributes: null,
    cancellationToken);
```

### 7.2 Backend RBAC + ACL + ABAC on behalf of a user

```csharp
using Asterloom.Sdk.Authorization;
using Asterloom.Targeting;

// transport obtains a Client Credentials token for the business Confidential Client.
var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
var attributes = new Dictionary<string, TargetingValue>
{
    ["subject.department"] = TargetingValue.From(profile.Department),
    ["resource.amount"] = TargetingValue.From((double)order.Amount),
    ["resource.ownerId"] = TargetingValue.From(order.OwnerSubjectId),
    ["context.mfa"] = TargetingValue.From(authContext.MfaVerified),
};

var decision = await authorization.CheckAccessAsync(
    actorId: user.FindFirstValue("sub"),
    permission: "orders.refund",
    scope: new AsterloomAuthorizationScope(tenantId, applicationId),
    resourceType: "order",
    resourceId: order.Id,
    attributes,
    cancellationToken);

if (!decision.Allowed)
{
    return Results.Forbid();
}
```

`CheckPermissionAsync(permission, scope)` remains the convenience API for checks without resources or attributes.

## 8. Web administration

Route: `/authorization/roles`

1. Select the Tenant and Application UUID at the top.
2. Create business actions under **Application permissions**. System Permissions remain read-only and are
   available to Roles.
3. Create least-privilege business roles under **Roles**.
4. Assign a Role to a user `sub` or service Client ID under **Role bindings**.
5. Add Actor/Role/Any, Allow/Deny, ACL selectors, and optional ABAC conditions under **Policy rules**.
6. Enter resource and JSON attributes under **Simulator & revisions**, inspect the decision, then review immutable
   revisions.

The simulator is a trusted administrative diagnostic and accepts attributes. “Check my access” intentionally omits
custom attributes, matching User Token behavior.

The console covers all Permission, Role, Binding, Policy, Revision, and Simulation Admin APIs, including create,
update, archive, and restore. Writes retain CSRF protection and `expectedVersion` optimistic concurrency.

## 9. API summary

| Purpose | JSON Transcoding | Caller |
| --- | --- | --- |
| Runtime decision | `POST /api/v1/authorization:check` | User self-check or Application Confidential Client on behalf of a member |
| Admin simulation | `POST /api/v1/authorization:simulate` | administrator with `authorization.simulation.execute` |
| Permission | `/api/v1/authorization/permissions` | matching `authorization.permission.*` |
| Role | `/api/v1/authorization/roles` | matching `authorization.role.*` |
| Binding | `/api/v1/authorization/role-bindings` | matching `authorization.binding.*` |
| Policy | `/api/v1/authorization/policies` | matching `authorization.policy.*` |
| Revision | `/api/v1/authorization/revisions` | `authorization.revision.read` |

Every endpoint is also native gRPC. Proto remains the only contract source; do not create divergent HTTP DTOs.

## 10. Archive, deny, and operations rules

- Default deny applies. Any matching explicit Deny overrides Role or Policy Allows.
- `Any` means any authenticated actor, never anonymous access.
- Archived Permissions, Roles, Bindings, and Policies stop participating; restoration reactivates them.
- A Role or Policy can reference only an active Permission. Archiving a Permission centrally disables the action.
- Every management change writes a Revision. Reread after `expectedVersion` conflicts instead of overwriting.
- Check every high-risk operation remotely. Low-risk UI pre-checks remain UX only.
- Keep a second administrator or break-glass identity before changing the last authorization manager.

## 11. Runnable example and implementation

The Reference Backend
[ReferenceBusinessAuthorizationEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceBusinessAuthorizationEndpoints.cs)
implements the complete flow: a Public Client User Token protects the refund API, the backend loads department and
order facts from its own PostgreSQL tables, and its Confidential Client calls `CheckAccessAsync`.

- Runtime protocol: [authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- Admin protocol: [authorization_admin.proto](../../Proto/Asterloom/authorization/v1/authorization_admin.proto)
- Types: [authorization_types.proto](../../Proto/Asterloom/authorization/v1/authorization_types.proto)
- Decision engine: [AuthorizationDecisionService.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationDecisionService.cs)
- C# SDK: [AsterloomAuthorizationClient.cs](../../Backend/Asterloom.Sdk.Authorization/AsterloomAuthorizationClient.cs)
- Web: [authorization-workspace.tsx](../../Frontend/features/authorization/authorization-workspace.tsx)
- Semantic tests: [AuthorizationTests.cs](../../Backend/Tests/Asterloom.UnitTests/AuthorizationTests.cs)
