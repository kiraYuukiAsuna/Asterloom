# Asterloom 模块使用文档

[简体中文](README.zh-CN.md) | [English](README.md)

本目录按用户可见能力维护 Asterloom 的实施与使用说明。每篇文档以当前仓库中的 .NET/C# 后端、
C# SDK、Next.js Web 管理后台和 Protobuf 契约为准；Rust、Go、C++ 不在当前范围。

## 阅读顺序

首次建设一个应用时，建议依次阅读：

1. [Platform](Platform.zh-CN.md)：建立 Tenant、Application、Environment 作用域。
2. [Identity](Identity.zh-CN.md)：注册 Passport Client，选择用户登录或服务登录。自带注册/登录页面的业务还应阅读
   [业务应用统一账号接入](Identity-Business-Integration.zh-CN.md)。
3. [Authorization](Authorization.zh-CN.md)：定义业务 Permission，并组合 RBAC、ACL 与 ABAC。
4. 根据业务选择 Feature、Config、Release、Analytics、Telemetry、Storage 等能力。
5. [RPC/HTTP](Rpc-Http.zh-CN.md) 与 [Web Console/BFF](Web-Console-Bff.zh-CN.md)：理解传输和浏览器安全边界。
6. [Persistence](Persistence.zh-CN.md)、[Audit](Audit.zh-CN.md)、[Operations](Operations.zh-CN.md)：完成部署与运维基线。

## 模块目录

| 能力 | 文档 | Web 入口 | C# 接入 |
| --- | --- | --- | --- |
| Platform | [平台作用域](Platform.zh-CN.md) | `/tenants` | gRPC/JSON Admin API |
| Identity | [Passport 与账号](Identity.zh-CN.md) | `/identity/users` | `Asterloom.Sdk.Identity` |
| 业务账号接入 | [Public Client 登录、业务 API 验证、注册与成员关系](Identity-Business-Integration.zh-CN.md) | Identity + Authorization 工作区 | `Asterloom.Sdk.Identity` + `Asterloom.Sdk.Identity.AspNetCore` |
| Authorization | [RBAC、ACL 与 ABAC](Authorization.zh-CN.md) | `/authorization/roles` | `Asterloom.Sdk.Authorization` |
| Targeting / Rollout | [定向与稳定灰度](Targeting-Rollout.zh-CN.md) | `/targeting/segments` | `Asterloom.Sdk.Targeting` |
| Feature Flag | [功能开关](Feature-Flags.zh-CN.md) | `/features` | `Asterloom.Sdk.Feature` + OpenFeature |
| Dynamic Config | [动态配置](Dynamic-Config.zh-CN.md) | `/config` | `Asterloom.Sdk.Config` |
| Desktop Update | [桌面自动更新](Desktop-Updates.zh-CN.md) | `/channels`、`/artifacts`、`/releases` | `Asterloom.Sdk.Release` + Velopack |
| Analytics | [产品埋点分析](Analytics.zh-CN.md) | `/analytics/schemas`、`/analytics/explorer` | `Asterloom.Sdk.Analytics` |
| Telemetry | [技术可观测性](Telemetry.zh-CN.md) | `/telemetry/sources`、`/telemetry/health` | `Asterloom.Sdk.Telemetry` + OpenTelemetry |
| Mail | [事务型应用邮件](Mail.zh-CN.md) | `/mail/accounts`、`/mail/deliveries` | `Asterloom.Sdk.Mail` + MailKit |
| RPC / HTTP | [统一传输与契约](Rpc-Http.zh-CN.md) | `/operations/apis` | `Asterloom.Sdk.Rpc` |
| File Storage | [文件与对象存储](File-Storage.zh-CN.md) | `/storage/buckets`、`/storage/objects` | `Asterloom.Sdk.Storage` |
| Persistence | [PostgreSQL 与迁移](Persistence.zh-CN.md) | `/operations/health` | Npgsql / 模块 Store |
| Audit | [管理操作审计](Audit.zh-CN.md) | `/audit` | Audit Admin API |
| Operations | [API 目录与健康检查](Operations.zh-CN.md) | `/operations/apis`、`/operations/health` | Operations Admin API |
| Web Console / BFF | [浏览器管理后台](Web-Console-Bff.zh-CN.md) | 全部管理路由 | Next.js BFF + Redis Session |

## 每篇文档的共同约定

- `Proto/Asterloom` 是 API 唯一契约源；原生 gRPC 与 JSON Transcoding 共享同一业务实现。
- 所有管理写操作都需要 Bearer 身份、相应 Permission、CSRF 保护（Web）和乐观并发版本。
- `Tenant → Application → Environment` 是主要业务作用域；未特别说明的运行面能力都在该作用域内。
- Web 管理后台应覆盖全部 Admin RPC；覆盖映射由 `Docs/Protocol/admin-api-coverage.yaml` 验证。
- SDK 示例默认已经通过 Identity 获取 Token，并创建了共享的 `AsterloomAuthenticatedTransport`。
- 文档描述的是当前已实现语义；已保留但尚未生效的字段会明确标注，不能按未来行为使用。

## 总览文档

- [技术架构与实施基线](../Architecture.md)
- [功能使用指南](../Feature-Guide.zh-CN.md)
- [全能力参考应用](../Reference-Application.md)
- [标准协议端点](../Protocol/standard-endpoints.md)
