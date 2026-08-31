# Authorization: Roles, Bindings, and Policies

[English](Authorization.md) | [简体中文](Authorization.zh-CN.md) | [Module index](README.md)

Authorization decides whether an authenticated actor may perform a permission in a scope. Identity produces the
actor and token, Platform defines the hierarchy, and Authorization uses Casbin.NET for the final decision.

## 1. Decision model

```text
Actor + trusted Passport role
  ├─ Role Binding → Role → Permission Allow
  └─ Policy Rule (Actor / Role / Any, Allow / Deny)
           + requested Tenant/Application/Environment
                └─ deny overrides → Allowed / Denied + explanation
```

| Concept | Meaning |
| --- | --- |
| Permission | Stable platform action key such as `feature.flag.evaluate` |
| Role | Permission collection; either system-owned or custom |
| Role Binding | Assigns an actor and role at a Global/Tenant/Application/Environment scope |
| Policy Rule | Directly allows or denies one permission for an Actor, Role, or Any actor |
| Revision | Immutable summary of each role, binding, or policy change |

The model defaults to deny. Any matching Deny overrides all matching Allows.

## 2. Scope inheritance

```text
Global
  └─ Tenant
       └─ Application
            └─ Environment
```

A parent binding or policy covers child requests; an Environment scope matches only that Environment. An
Environment requires Application and Tenant, and an Application requires Tenant.

Identity Application Membership controls whether a user may enter an application, while roles and policies control
actions inside it. Most business users need both. An application-bound user token is constrained to its
`tenant_id`/`application_id`; it cannot request a decision for another application, and a removed membership is
rejected immediately. Client Credentials tokens represent the bound service client and do not require a user
membership.

## 3. Web workflow

Route: `/authorization/roles`

1. Select required actions from the Permission Catalog.
2. Prefer an existing least-privilege role or create one.
3. Bind actor, role, and the narrowest useful scope.
4. Add a Policy only for exceptions such as a specific actor Deny or an Any actor Allow.
5. Use Simulation with actor, scope, and permission to inspect the result and matches.
6. Review Policy Revisions for actor, summary, and snapshot hash.

The Web console covers Permission List; Role List/Create/Update/Archive/Restore; Binding List/Set/Remove; Policy
List/Create/Update/Archive/Restore; Revision List; and Simulation.

## 4. C# permission checks

The server authorization interceptor enforces API permissions. An application may also check explicitly for UI or
business behavior:

```csharp
using Asterloom.Sdk.Authorization;

var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
var decision = await authorization.CheckPermissionAsync(
    "storage.object.upload",
    new AsterloomAuthorizationScope(tenantId, applicationId, environmentId),
    cancellationToken);

if (!decision.Allowed)
{
    throw new InvalidOperationException(decision.Reason);
}
```

This improves UX but never replaces server enforcement. Hiding a button is not access control.

The `actorId` must match the authenticated token subject. Business application A cannot ask for business B's scope,
even if it sends B's identifiers in the request body.

## 5. System and custom roles

System roles include `super-administrator`, `tenant-administrator`, `operator`, `developer`, and `viewer`.
Trusted Passport role claims map to their corresponding system roles. Business code must not modify them.

Use custom roles for least-privilege collections, such as upload and download in one Environment. Do not grant
ordinary applications `*` or `super-administrator` indefinitely.

## 6. Policy guidance

- Role bindings are the normal grant mechanism.
- Policy Allow is useful for small exceptions or runtime access for `Any` actor.
- Policy Deny supports emergency blocking, separation of duties, and high-risk controls.
- `Any` means any **authenticated** actor; it does not make an API anonymous.
- Archived policies and roles stop participating, while revisions remain durable.
- Writes use `expectedVersion`; reread after a conflict instead of overwriting blindly.

## 7. Permissions and lockout prevention

- `authorization.permission.read`
- `authorization.role.read/create/update/archive/restore`
- `authorization.binding.read/set/remove`
- `authorization.policy.read/create/update/archive/restore`
- `authorization.revision.read`
- `authorization.simulation.execute`

Before changing administrator roles or policies, validate with a second controlled administrator or break-glass
path so the last authorization manager cannot be removed accidentally.

## 8. Related implementation

- Runtime protocol: [authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- Admin protocol: [authorization_admin.proto](../../Proto/Asterloom/authorization/v1/authorization_admin.proto)
- Types: [authorization_types.proto](../../Proto/Asterloom/authorization/v1/authorization_types.proto)
- Decision engine: [AuthorizationDecisionService.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationDecisionService.cs)
- Permission catalog: [AuthorizationCatalog.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationCatalog.cs)
- C# SDK: [AsterloomAuthorizationClient.cs](../../Backend/Asterloom.Sdk.Authorization/AsterloomAuthorizationClient.cs)
- Web: [authorization-workspace.tsx](../../Frontend/features/authorization/authorization-workspace.tsx)
