# Platform：Tenant、Application 与 Environment

[简体中文](Platform.zh-CN.md) | [English](Platform.md) | [模块索引](README.zh-CN.md)

Platform 是所有 Asterloom 能力共用的业务作用域与资源生命周期内核。这里的 Platform 不表示
Windows/macOS/Linux；桌面运行平台由 Release Artifact 的 `targetRuntimeId` 表示。

## 1. 资源层级

```text
Tenant
  ├─ Tenant Membership
  └─ Application
       └─ Environment
            ├─ Targeting / Feature / Config / Release
            ├─ Analytics / Telemetry
            └─ 其他环境级资源
```

| 资源 | 用途 | 稳定字段 |
| --- | --- | --- |
| Tenant | 组织、客户或安全边界 | `id`、创建后不可变的 `slug` |
| Application | 一个产品或服务 | `id`、创建后不可变的 `slug` |
| Environment | development/staging/production 隔离 | `id`、`slug`、`environmentType` |
| Membership | Actor 是否可见并进入某 Tenant | `tenantId + actorId` |

不要用 Environment 表达操作系统，也不要为 `win-x64` 单独建立 Application。一个桌面产品通常只有
一个 Application，同一 Release 中用多个 RID Artifact 区分平台。

## 2. Web 使用

路由：`/tenants`

1. 创建 Tenant，确定不可变 Slug。
2. 在 Tenant 内创建 Application。
3. 为 Application 创建 development、staging、production 等 Environment。
4. 生产 Environment 可设置 `isProtected`，防止误归档。
5. 在 Tenant Membership 中添加或移除 Actor。
6. 其他模块的全局 Scope Selector 会复用这里创建的三个 UUID。

Web 支持 Tenant、Application、Environment 的 List/Create/Update/Archive/Restore，以及 Membership
的 List/Set/Remove。归档保留历史，不是物理删除。

创建 Application 后，控制台可以打开应用初始化向导。默认预设会创建受保护的 `production`
Environment、OIDC Client/Scope、应用访问 Permission 与运行时 Allow Policy、以及 Feature、Config、
Storage、Release、Analytics、Telemetry 的起始资源；需要返回 Client Secret 或 Write Key 的步骤只展示一次。
向导生成的业务 Permission 会在租户/应用 slug 前固定加 `app.` 前缀，例如
`app.acme-payments-checkout.access`，避免租户 slug 以 `analytics-`、`telemetry-` 等保留模块名开头时
与 Asterloom 系统权限命名空间冲突。

## 3. 生命周期与并发

- Slug 创建后不可修改；Display Name 可以更新。
- Update、Archive、Restore 和 Membership 变更都使用 `expectedVersion` 乐观并发。
- Protected Environment 必须先取消保护才能 Archive。
- 运行面读取要求 Tenant、Application、Environment 都处于 Active；上层资源归档后，下层数据仍保留，
  但运行面不可用。
- 恢复下层资源前应先确保所有父资源已经 Active。

## 4. API 与接入

当前没有单独的 Platform Runtime SDK。管理程序使用
[`platform_admin.proto`](../../Proto/Asterloom/platform/v1/platform_admin.proto) 生成的 gRPC Client，
或调用其 JSON Transcoding 路由。应用运行时通常只保存三个 ID，并把它们传给各能力 SDK：

```csharp
var featureScope = new AsterloomFeatureScope(tenantId, applicationId, environmentId);
var configScope = new AsterloomConfigScope(tenantId, applicationId, environmentId);
var releaseScope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
```

Scope ID 应由部署配置注入，不能通过 Display Name 临时查询和猜测。

## 5. 权限

权限按资源和动作拆分：

- `platform.info.read`
- `platform.tenant.read/create/update/archive/restore`
- `platform.application.read/create/update/archive/restore`
- `platform.environment.read/create/update/archive/restore`
- `platform.tenant.membership.read/set/remove`

Membership 决定 Tenant 可见性，Authorization Role/Policy 决定具体操作权限；两者不能互相替代。

## 6. 实施建议

- 开发、预发和生产使用独立 Environment，不要只靠 Feature Flag 模拟环境隔离。
- 一个产品保持一个稳定 Application；只有安全、生命周期或数据所有权确实独立时才拆分。
- 生产环境开启保护，并限制 `platform.environment.update/archive`。
- 在 CI、应用配置和审计事件中使用 UUID/Slug，Display Name 只用于展示。
- Archive 前先检查 Release、Config、Feature、Storage 等下游依赖。

## 7. 相关实现

- Protocol：[platform_admin.proto](../../Proto/Asterloom/platform/v1/platform_admin.proto)
- 业务规则：[PlatformManagementService.cs](../../Backend/Asterloom.Module/Platform/PlatformManagementService.cs)
- 数据模型：[PlatformResources.cs](../../Backend/Asterloom.Module/Platform/Model/PlatformResources.cs)
- Web：[platform-workspace.tsx](../../Frontend/features/platform/platform-workspace.tsx)
- 架构说明：[Architecture.md](../Architecture.md)
