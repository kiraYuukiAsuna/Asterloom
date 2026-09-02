# Analytics：产品事件、Schema 与聚合

[简体中文](Analytics.zh-CN.md) | [English](Analytics.md) | [模块索引](README.zh-CN.md)

Analytics 用于回答产品和业务问题，例如功能使用率、漏斗步骤和结果事件。它与 Telemetry 不同：
Analytics 是业务事件，Telemetry 是 Trace、Metric、Log、Exception 和技术健康。

## 1. 数据流

```text
Application + Write Key
  → SDK queue / batch / gzip / retry
  → IngestEvents
  → Schema validation + sensitive-field redaction + EventId deduplication
  → PostgreSQL retention store
  → Web Explorer / aggregate query / CSV export
```

## 2. Web 工作流

路由：`/analytics/schemas`、`/analytics/explorer`

> **路径命名说明：** 浏览器地址栏中的 Web 页面路由仍使用 `/analytics/...`；页面在后台发出的
> 管理 API 请求使用 `/api/v1/.../insights/...`。这是有意设计的不一致，用于避免广告或隐私扩展
> 将事件查询管理请求误判为追踪流量。原 `/api/v1/.../analytics/...` 管理 API 作为兼容别名保留，
> SDK 运行时上报路径仍为 `/api/v1/analytics/events:batch`。

### Event Schema

1. 为稳定 Event Name 创建 JSON Schema。
2. 设置 Display Name、Description 和 1–3650 天 Retention。
3. 对敏感属性标记 `x-asterloom-sensitive: true`。
4. Update、Archive/Restore Schema；归档后不应继续上报。

### Write Key

- 为每个应用/环境/生产者创建独立 Write Key。
- Secret 只在 Create/Rotate 时返回一次，立即保存到 Secret Manager。
- 泄漏时 Rotate；生产者下线后 Revoke。

### Explorer

- 按 Event Name、Actor、Event ID、时间范围筛选。
- 查看已经脱敏的 Properties/Context。
- 按时间间隔查询 Event Count 与 Unique Actors。
- 导出受限制行数的 CSV。

## 3. C# SDK

```csharp
using Asterloom.Sdk.Analytics;

await using var analytics = new AsterloomAnalyticsClient(
    transport.HttpClient,
    new AsterloomAnalyticsClientOptions
    {
        WriteKey = analyticsWriteKey,
        BatchSize = 20,
        FlushInterval = TimeSpan.FromSeconds(5),
        OfflineQueuePath = offlineQueuePath,
        CommonContext = new Dictionary<string, object?>
        {
            ["platform"] = "win-x64",
            ["version"] = appVersion,
        },
    });

string eventId = await analytics.TrackAsync(
    "checkout.completed",
    new { orderId, amount, currency = "CNY" },
    new AsterloomAnalyticsIdentity(
        ActorId: userId,
        SessionId: sessionId),
    cancellationToken: cancellationToken);

var result = await analytics.FlushAsync(cancellationToken);
```

默认 Batch Size 20、Flush Interval 5 秒、最多重试 3 次，1 KiB 以上 Payload 使用 GZip。`DisposeAsync`
会在受限 Shutdown Timeout 内尝试最后一次 Flush。

## 4. 可靠性与去重

- SDK 为每个事件生成 UUIDv7 `EventId`；服务端用它进行幂等去重。
- 暂时错误采用指数退避，并尊重 `Retry-After`。
- Queue 满时 `TrackAsync` 失败，避免无限内存增长。
- 配置 `OfflineQueuePath` 后，事件以本地队列跨进程重启保留；当前文件不是自动加密的，路径必须由应用
  做访问控制，敏感应用应增加加密存储实现。
- `DeliveryFailed` 报告 Schema/业务拒绝；`FlushResult` 区分 Accepted、Rejected、Deduplicated、Remaining。
- 程序崩溃仍可能丢失最后未持久化事件；Analytics 不适合作为财务账本或唯一业务事实来源。

## 5. Schema 与隐私

- Event Name 在 SDK 中规范为小写，长度 2–100。
- Properties 必须满足当前 Active Schema；错误事件按条拒绝，不应拖垮整个批次。
- `x-asterloom-sensitive` 用于服务端持久化前脱敏，但不等于允许发送任何 PII。
- 禁止发送密码、Token、银行卡号、私钥、完整请求/响应和无限制个人信息。
- Actor ID 优先使用内部不可读 ID；匿名用户使用稳定 Anonymous ID，并定义登录后的身份合并策略。
- Context 保持低体积、受控字段，不要直接序列化整个用户或设备对象。

## 6. 权限与认证

运行时 Ingestion 使用 `X-Asterloom-Write-Key`，Write Key 只允许写入其 Scope。管理和读取使用 Bearer
Token 与以下权限：

- `analytics.schema.read/create/update/archive/restore`
- `analytics.retention.update`
- `analytics.write-key.read/create/rotate/revoke`
- `analytics.event.read`、`analytics.query.execute`、`analytics.event.export`

Write Key 不是管理 Token，也不能查询事件。不要把同一个 Write Key 跨 Environment 复用。

## 7. 上线检查

- [ ] 事件先登记 Schema，再发布生产者。
- [ ] Event Name、字段类型和单位形成版本化数据契约。
- [ ] 敏感字段已删除或最小化，必要字段标记脱敏。
- [ ] Write Key 按应用/环境隔离，并具备轮换流程。
- [ ] 离线队列目录受保护，退出时 Flush。
- [ ] Retention 满足业务、隐私和合规要求。
- [ ] 业务关键状态仍由事务数据库保存，而不是依赖 Analytics。

## 8. 相关实现

- Runtime Protocol：[analytics.proto](../../Proto/Asterloom/analytics/v1/analytics.proto)
- Admin Protocol：[analytics_admin.proto](../../Proto/Asterloom/analytics/v1/analytics_admin.proto)
- Types：[analytics_types.proto](../../Proto/Asterloom/analytics/v1/analytics_types.proto)
- SDK：[AsterloomAnalyticsClient.cs](../../Backend/Asterloom.Sdk.Analytics/AsterloomAnalyticsClient.cs)
- Ingestion：[AnalyticsIngestionService.cs](../../Backend/Asterloom.Module.Analytics/AnalyticsIngestionService.cs)
- Schema Validator：[AnalyticsSchemaValidator.cs](../../Backend/Asterloom.Module.Analytics/AnalyticsSchemaValidator.cs)
- Web：[analytics-workspace.tsx](../../Frontend/features/analytics/analytics-workspace.tsx)
- Telemetry 对比：[Telemetry.zh-CN.md](Telemetry.zh-CN.md)
