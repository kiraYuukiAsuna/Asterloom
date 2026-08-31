# Authorization：Role、Binding 与 Policy

[简体中文](Authorization.zh-CN.md) | [English](Authorization.md) | [模块索引](README.zh-CN.md)

Authorization 负责“一个已经认证的 Actor 能否在某个 Scope 执行某项 Permission”。Identity 产生
Actor 和 Token，Platform 定义资源层级，Authorization 使用 Casbin.NET 做最终决策。

## 1. 决策模型

```text
Actor + Trusted Passport Role
  ├─ Role Binding → Role → Permission Allow
  └─ Policy Rule (Actor / Role / Any, Allow / Deny)
           + requested Tenant/Application/Environment
                └─ Deny 优先 → Allowed / Denied + explanation
```

| 概念 | 含义 |
| --- | --- |
| Permission | 平台维护的稳定动作键，如 `feature.flag.evaluate` |
| Role | 一组 Permission；包含 System Role 和自定义 Role |
| Role Binding | 把一个 Actor 的 Role 限定到 Global/Tenant/Application/Environment Scope |
| Policy Rule | 对 Actor、Role 或 Any Actor 直接 Allow/Deny 一项 Permission |
| Revision | 每次 Role/Binding/Policy 变更的不可变快照摘要 |

默认拒绝：没有任何匹配 Allow 时返回 Denied。任意匹配 Deny 会覆盖所有 Allow。

## 2. Scope 继承

```text
Global
  └─ Tenant
       └─ Application
            └─ Environment
```

上层 Binding/Policy 可覆盖下层请求；Environment Scope 只匹配该 Environment。填写 Environment 时必须
同时填写 Application 和 Tenant，填写 Application 时必须填写 Tenant。

Identity Application Membership 控制用户能否进入某个应用，Role/Policy 控制进入后的动作权限；大多数业务
用户需要同时具备两者。应用绑定的用户 Token 被限制在自身 `tenant_id`/`application_id`，不能请求其他应用的
权限决策；成员关系移除后立即拒绝。Client Credentials Token 代表绑定的服务 Client，不要求用户成员关系。

## 3. Web 使用

路由：`/authorization/roles`

1. 从 Permission Catalog 选择所需动作。
2. 优先使用或创建最小权限 Role。
3. 创建 Role Binding，把 Actor、Role 和最小 Scope 绑定。
4. 只在需要例外时创建 Policy Rule，例如特定 Actor Deny 或 Any Actor Allow。
5. 使用 Simulator 输入 Actor、Scope、Permission 验证结果和匹配来源。
6. 查看 Policy Revisions，确认变更人和快照 Hash。

Web 支持 Permission List，Role List/Create/Update/Archive/Restore，Binding List/Set/Remove，Policy
List/Create/Update/Archive/Restore，Revision List 和 Simulation。

## 4. C# 权限检查

服务端 API 已由 Authorization Interceptor 自动检查。应用若需要控制自身 UI 或业务分支，可显式调用：

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

该检查用于用户体验，不替代服务端强制授权。不要仅隐藏按钮后就认为资源安全。

`actorId` 必须与已认证 Token 的 Subject 相同。业务 A 即使在请求体中填写业务 B 的标识，也不能检查 B 的作用域。

## 5. System Role 与自定义 Role

系统提供 `super-administrator`、`tenant-administrator`、`operator`、`developer`、`viewer` 等 Role，
并把可信 Passport Role Claim 映射到对应 System Role。System Role 不应被业务修改。

自定义 Role 适合业务最小权限集合，例如只允许上传和下载某 Environment 的文件。避免长期给普通
应用 `*` 或 `super-administrator`。

## 6. Policy 使用原则

- Role Binding 是常规授权入口。
- Policy Allow 适合少量例外或 `Any` Actor 的运行面权限。
- Policy Deny 适合紧急阻断、职责分离和高风险动作保护。
- `Any` 表示任意**已认证** Actor，不会创建匿名 API。
- Policy 和 Role 归档后不参与决策，但历史 Revision 保留。
- 修改时使用 `expectedVersion`；并发冲突后重新读取，不要盲目重试覆盖。

## 7. 权限与防止锁死

管理 Authorization 自身需要：

- `authorization.permission.read`
- `authorization.role.read/create/update/archive/restore`
- `authorization.binding.read/set/remove`
- `authorization.policy.read/create/update/archive/restore`
- `authorization.revision.read`
- `authorization.simulation.execute`

变更管理员 Role/Policy 前，先用另一个受控管理员账号或 Break-glass 方案验证，避免删除最后一个可管理
Authorization 的主体。

## 8. 相关实现

- Runtime Protocol：[authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- Admin Protocol：[authorization_admin.proto](../../Proto/Asterloom/authorization/v1/authorization_admin.proto)
- 类型：[authorization_types.proto](../../Proto/Asterloom/authorization/v1/authorization_types.proto)
- 决策引擎：[AuthorizationDecisionService.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationDecisionService.cs)
- Permission Catalog：[AuthorizationCatalog.cs](../../Backend/Asterloom.Module.Authorization/AuthorizationCatalog.cs)
- C# SDK：[AsterloomAuthorizationClient.cs](../../Backend/Asterloom.Sdk.Authorization/AsterloomAuthorizationClient.cs)
- Web：[authorization-workspace.tsx](../../Frontend/features/authorization/authorization-workspace.tsx)
