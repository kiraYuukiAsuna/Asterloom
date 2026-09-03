# Authorization：RBAC、ACL 与 ABAC

[简体中文](Authorization.zh-CN.md) | [English](Authorization.md) | [模块索引](README.zh-CN.md)

Authorization 回答一个问题：已经通过 Identity 认证的 Actor，能否在指定
`Tenant → Application → Environment` 作用域内，对某个业务资源执行某项动作。

Asterloom 同时支持三种互补模型，而不是把 ABAC 当成 ACL：

| 模型 | 解决的问题 | Asterloom 表示方式 | 示例 |
| --- | --- | --- | --- |
| RBAC | 这个用户属于什么职责？ | Permission → Role → Role Binding | `finance-operator` 拥有 `orders.refund` |
| ACL | 他能操作哪个具体资源？ | Policy Rule 的 `resourceType` + `resourceId` | 只允许 `order/order-42` |
| ABAC | 当前主体、资源和上下文是否满足条件？ | Policy Rule 的 typed `condition` | 部门为 finance 且金额小于 5000 |

ACL 是“资源实例列表/选择器”，ABAC 是“属性表达式”。二者都附着在统一 Policy Rule 上，可以单独使用，也可以与 RBAC
叠加。底层使用 Casbin.NET 完成作用域、权限、资源和 Allow/Deny 决策，ABAC 条件复用 Asterloom Targeting 的强类型规则引擎。

## 1. 完整决策模型

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
       permission active + scope matches + resource matches + condition matches
                ↓
       any Deny wins; otherwise any Allow wins; otherwise default Deny
```

返回值包含 `allowed`、可读 `reason`、命中的 Policy ID 和 Role Key。决策不缓存管理员提交的自定义属性，策略、角色、
成员关系或 Permission 状态改变后，下次远程检查立即使用最新快照。

## 2. Application Permission Catalog

Asterloom 自身 API 的 `platform.*`、`feature.*`、`storage.*` 等 System Permission 由平台维护，不能修改。每个业务
Application 可以创建自己的 Permission，例如：

- `orders.read`
- `orders.refund`
- `invoice.approve`
- `project.member.invite`

业务 Permission 必须是小写、带分隔符的动作键，且不能使用 Asterloom 已占用的模块命名空间。它严格属于创建时指定的
Tenant/Application，同名 Permission 可以存在于不同 Application，但角色和策略不能跨 Application 引用它。

归档 Permission 会让它立即停止参与决策，即使某个活动 Role 仍保留该键；恢复后原 Role/Policy 无需重建。归档用于下线
业务动作，不会删除历史 Revision。

## 3. RBAC

业务自定义 Role 必须属于一个确定的 Tenant/Application，不能创建 Global 自定义 Role。Role 聚合 System Permission
和同一 Application 的活动业务 Permission。Role Binding 再把 Role 赋给一个 Actor，并可缩小到 Application 下的某个
Environment。

```text
Role owner scope: tenant A / application Orders
Binding scope:    tenant A / application Orders / environment Production
Request scope:    tenant A / application Orders / environment Production  → may match
Request scope:    tenant A / application CRM                              → never matches
```

System Role（`super-administrator`、`tenant-administrator`、`operator`、`developer`、`viewer`）用于 Asterloom
管理面。业务用户应优先使用 Application Role，不要把平台管理员角色当作业务角色。

## 4. ACL

Policy Rule 的资源选择器由两个字段组成：

- `resourceType`：规范化为小写，如 `order`、`document`、`project`。
- `resourceId`：业务系统中的稳定资源 ID，如 `order-42`；只有填写 `resourceType` 后才能填写。

空字段表示不限制资源。当前 ACL 使用**精确匹配**，不把 `*`、路径或正则解释为通配符。若需要“一类资源”，只填写
`resourceType`，让 `resourceId` 留空；若需要多条具体资源，为每个资源创建规则，或使用 ABAC 表达所有者、组织等属性。

示例：Actor 只有 `order/order-42` 的退款例外权限：

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

ABAC Condition 使用 Targeting 的 `ALL`/`ANY` 组合以及强类型操作符，支持 text、truth、numeric 的等于、不等于、集合、
包含、前后缀、数值比较、存在性和语义版本比较。属性名必须使用以下命名空间：

| 前缀 | 来源 | 示例 |
| --- | --- | --- |
| `subject.*` | 业务数据库中的用户资料或组织关系 | `subject.department=finance` |
| `resource.*` | 业务数据库中资源的权威数据 | `resource.amount=1200` |
| `context.*` | 本次请求由服务端验证的上下文 | `context.mfa=true` |
| `scope.*` | Asterloom 根据请求自动写入 | `scope.applicationId=...` |

Asterloom 总会覆盖并提供 `subject.id`、`resource.type`、`resource.id`、`scope.tenantId`、
`scope.applicationId`、`scope.environmentId`，调用方不能伪造这些权威字段。

最重要的信任边界：Public Client/User Token 只能检查自身，且**不能提交自定义 ABAC 属性**。只有绑定到该
Application 的 Confidential Client（业务后端）才能代表活动成员提交属性。业务后端必须从自己的数据库、已验证 Token
或可信风控结果构造属性；禁止把浏览器提交的 `department`、`ownerId`、`amount` 原样转发为授权事实。

## 6. 推荐业务调用链

```text
Desktop / mobile Public Client
  1. Authorization Code + S256 PKCE 登录 Passport
  2. 携带 user Access Token 调用 business API
                           ↓
Business backend
  3. 本地验证 JWT issuer / audience / tenant / application / user actor type
  4. 从自己的 DB 加载 user、order、organization 等权威属性
  5. 用自己的 Confidential Client service token 调 Asterloom CheckAccess
     actorId = user token sub
  6. 只在 decision.allowed=true 时执行业务操作
```

Public Client 的用户 Access Token 证明“谁在调用业务 API”；Confidential Client 的 Service Token 证明“哪个可信业务
后端在向 Asterloom 提交授权上下文”。它们是两个同时存在的身份，后者不能代替前者访问需要用户上下文的业务接口。

## 7. C# 接入

### 7.1 当前 Actor 的简单 RBAC/ACL 自检

适用于 UI 提示或无需业务属性的资源权限；服务端仍必须独立执行强制授权：

```csharp
using Asterloom.Sdk.Authorization;

var authorization = new AsterloomAuthorizationClient(userTransport.CallInvoker);
var decision = await authorization.CheckAccessAsync(
    actorId: null, // 从当前 User Token 的 sub 推断
    permission: "orders.read",
    scope: new AsterloomAuthorizationScope(tenantId, applicationId),
    resourceType: "order",
    resourceId: orderId,
    attributes: null,
    cancellationToken);
```

### 7.2 业务后端代表用户执行 RBAC + ACL + ABAC

```csharp
using Asterloom.Sdk.Authorization;
using Asterloom.Targeting;

// transport 使用业务 Confidential Client 的 Client Credentials Token。
var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);

// 全部来自业务后端验证后的 token、数据库或可信风控结果。
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

`CheckPermissionAsync(permission, scope)` 保留为不带资源和属性的便捷方法；复杂业务应使用 `CheckAccessAsync`。

## 8. Web 管理流程

入口：`/authorization/roles`

1. 在页面顶部选择 Tenant UUID 和 Application UUID。
2. 在 **Application permissions** 创建业务动作；System Permission 只读展示并可供 Role 选择。
3. 在 **Roles** 创建最小权限角色。
4. 在 **Role bindings** 把角色赋给用户 `sub` 或服务 Client ID。
5. 在 **Policy rules** 按需选择 Actor/Role/Any、Allow/Deny、ACL 资源和 ABAC Condition。
6. 在 **Simulator & revisions** 输入资源与 JSON 属性，验证组合结果并查看不可变 Revision。

模拟器代表管理面可信诊断，因此允许输入属性；“Check my access”故意不提交自定义属性，行为与普通 User Token 一致。

Web 覆盖 Permission、Role、Binding、Policy、Revision 与 Simulation 的全部 Admin API，包括创建、更新、归档和恢复，
写操作继续使用 CSRF 与 `expectedVersion` 乐观并发。

## 9. API 概览

| 用途 | JSON Transcoding | 调用身份 |
| --- | --- | --- |
| 运行时决策 | `POST /api/v1/authorization:check` | User 自检；或 Application Confidential Client 代表成员 |
| 管理面模拟 | `POST /api/v1/authorization:simulate` | 具备 `authorization.simulation.execute` 的管理员 |
| Permission | `/api/v1/authorization/permissions` | 对应 `authorization.permission.*` |
| Role | `/api/v1/authorization/roles` | 对应 `authorization.role.*` |
| Binding | `/api/v1/authorization/role-bindings` | 对应 `authorization.binding.*` |
| Policy | `/api/v1/authorization/policies` | 对应 `authorization.policy.*` |
| Revision | `/api/v1/authorization/revisions` | `authorization.revision.read` |

所有接口同时有原生 gRPC。以 Proto 为唯一契约，不要手写与其分叉的 HTTP DTO。

## 10. 归档、拒绝优先与运维原则

- 默认拒绝；任意匹配的显式 Deny 覆盖 Role 或 Policy Allow。
- `Any` 仅表示任意已认证 Actor，不会开放匿名访问。
- Permission、Role、Binding、Policy 归档后均不再参与决策；恢复后重新生效。
- Role/Policy 只能引用活动 Permission；归档 Permission 是集中关闭一个业务动作的安全开关。
- 每次管理变更写入 Revision；`expectedVersion` 冲突后应重新读取，不能盲目覆盖。
- 高风险业务操作每次远程检查；低风险列表/按钮可做 UX 级预检查，但不能替代业务 API 强制执行。
- 修改最后一个管理者权限前，准备第二个管理员或 break-glass 账号，避免锁死管理面。

## 11. 可运行示例与实现

Reference Backend 的
[ReferenceBusinessAuthorizationEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceBusinessAuthorizationEndpoints.cs)
实现了完整调用链：Public Client User Token 保护退款 API，业务后端从自己的 PostgreSQL 读取部门和订单属性，再用
Confidential Client 调 `CheckAccessAsync`。

- Runtime Protocol：[authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- Admin Protocol：[authorization_admin.proto](../../Proto/Asterloom/authorization/v1/authorization_admin.proto)
- 类型：[authorization_types.proto](../../Proto/Asterloom/authorization/v1/authorization_types.proto)
- 决策引擎：[AuthorizationDecisionService.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationDecisionService.cs)
- C# SDK：[AsterloomAuthorizationClient.cs](../../Backend/Asterloom.Sdk.Authorization/AsterloomAuthorizationClient.cs)
- Web：[authorization-workspace.tsx](../../Frontend/features/authorization/authorization-workspace.tsx)
- 语义测试：[AuthorizationTests.cs](../../Backend/Tests/Asterloom.UnitTests/AuthorizationTests.cs)
