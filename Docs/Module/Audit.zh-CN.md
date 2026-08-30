# Audit：管理操作审计

[简体中文](Audit.zh-CN.md) | [English](Audit.md) | [模块索引](README.zh-CN.md)

Audit 回答“谁在什么时候对什么管理资源做了什么，以及结果如何”。它记录控制面的敏感管理操作，
不是产品埋点，也不是应用日志或 Trace；后二者分别见 [Analytics](Analytics.zh-CN.md) 和
[Telemetry](Telemetry.zh-CN.md)。

## 1. 记录范围

gRPC `AuditInterceptor` 自动记录 `.admin.` Service 中名称以这些动词开头的一元 RPC：

```text
Create / Update / Archive / Restore / Set / Remove / Delete / Import
Publish / Rollback / Rotate / Revoke / Export / Invite / Resend
Suspend / Reactivate / Complete / Copy / Pause / Promote
```

因此 Tenant、用户、角色、Feature、Config、Storage、Release 等管理变更会自动进入审计。普通 List/Get
读取不会记录；导出审计日志本身会记录。Runtime API、Analytics Event 和 OpenTelemetry Signal 不属于
Audit Event。

每条记录包含：

| 字段 | 含义 |
| --- | --- |
| `actorId` | Token 中的 `sub`；缺失时为 `unknown` |
| `tenantId/applicationId/environmentId` | 可从请求或响应识别到的资源作用域 |
| `operation` | 完整 gRPC Method，例如 `/asterloom.feature.admin.v1.FeatureAdminService/PublishFlag` |
| `resourceType/resourceId` | 标准化资源类型与目标 ID |
| `requestId` | ASP.NET Trace Identifier，用于跨日志、Trace 和错误响应关联 |
| `outcome` | `Succeeded`、`Denied` 或 `Failed` |
| `errorCode` | 失败时的 Asterloom Error Code 或 gRPC Status |
| `changeSummary` | 请求包含的字段名列表，不是字段值 |
| `createdAt` | UTC 创建时间 |

拦截器故意不序列化请求值，因此不会把密码、Secret、Token 或业务载荷复制进 `changeSummary`。调用方仍
不得把敏感值放进资源 Key、ID 或其他会出现在操作元数据中的字段。

## 2. Web 管理

路由：`/audit`

Web 支持：

- 按 Actor ID、Operation、Outcome、Request ID 和时间范围搜索。
- 分页查看并打开单条事件详情。
- 使用同一组过滤条件导出 CSV。
- 从错误页面或 Telemetry 中复制 Request ID，再回到 Audit 定位管理操作。

List 默认每页 50 条，最大 100 条。CSV 默认最多 1,000 行，可指定 1–10,000 行；结果直接在 API
响应中返回，导出较大时间段时应缩小过滤范围。

## 3. API 与权限

| RPC | JSON Transcoding | Permission |
| --- | --- | --- |
| `ListAuditEvents` | `GET /api/v1/audit/events` | `audit.event.read` |
| `GetAuditEvent` | `GET /api/v1/audit/events/{auditEventId}` | `audit.event.read` |
| `ExportAuditEvents` | `POST /api/v1/audit/events:export` | `audit.event.export` |

Audit 没有单独的客户端 SDK。管理员可使用 Web、生成的 gRPC Client 或 JSON API。应用自身不要调用
Admin API 写入自定义审计事件；需要产品事件用 Analytics，需要技术事件用 Telemetry。

JSON 查询示例：

```http
GET /api/v1/audit/events?actorId=USER_ID&outcome=AUDIT_OUTCOME_FAILED&pageSize=50
Authorization: Bearer <admin-access-token>
```

导出请求：

```json
{
  "requestId": "REQUEST_ID",
  "maximumRows": 1000
}
```

## 4. 结果与故障语义

- RPC 成功后记录 `Succeeded`；进入 gRPC 拦截器的细粒度权限拒绝记录 `Denied`；其他异常记录 `Failed`。
- 缺失或无效 Bearer Token 可能先被 ASP.NET Endpoint Authorization 拒绝，尚未进入 Audit Interceptor，
  因而不能假设每次匿名探测都会生成 Audit Event；边界访问日志仍需由 Nginx/Server 保留。
- 审计写入使用独立的 `CancellationToken.None`，尽量在请求结束前持久化。
- 当前实现若审计 Store 写入失败，会记录 Critical Log，但不会反向改变原管理 RPC 的结果。生产必须对
  `AuditInterceptor` 的 Critical Log 告警，不能把“业务成功”误认为“审计一定成功”。
- 当前没有修改或删除 Audit Event 的 Admin API，也没有内置在线归档页面。保留周期、数据库分区、备份
  和合规导出由运维策略负责。

## 5. 安全与运维检查

- `audit.event.read` 与 `audit.event.export` 只授予安全或审计人员，导出权限更严格。
- CSV 仍包含 Actor、资源与请求关联信息，应加密保存、限制分享并按保留策略销毁。
- 对 Denied/Failed 突增、敏感权限变更、Client Secret Rotate 和大批量 Export 建立告警。
- Nginx、BFF、Server 和 Telemetry 应透传同一个 `X-Request-ID`，便于端到端关联。
- 定期抽查关键 Admin RPC 是否出现在 Audit，并验证数据库备份可以恢复审计记录。

## 6. 相关实现

- Admin Protocol：[audit_admin.proto](../../Proto/Asterloom/audit/v1/audit_admin.proto)
- Types：[audit_types.proto](../../Proto/Asterloom/audit/v1/audit_types.proto)
- 拦截器：[AuditInterceptor.cs](../../Backend/Asterloom.Module.Rpc/Auditing/AuditInterceptor.cs)
- 管理服务：[AuditManagementService.cs](../../Backend/Asterloom.Module/Auditing/AuditManagementService.cs)
- PostgreSQL Store：[PostgreSqlAuditStore.cs](../../Backend/Asterloom.Module.Infrastructure/Auditing/PostgreSqlAuditStore.cs)
- Web：[audit-workspace.tsx](../../Frontend/features/audit/audit-workspace.tsx)
