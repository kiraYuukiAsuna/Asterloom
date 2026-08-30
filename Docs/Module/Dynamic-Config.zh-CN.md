# Dynamic Config：动态配置与 Snapshot

[简体中文](Dynamic-Config.zh-CN.md) | [English](Dynamic-Config.md) | [模块索引](README.zh-CN.md)

Dynamic Config 用于不发布新版本就调整应用行为参数。它提供类型化 Entry、Draft/Publish、Targeted
Value、不可变 Revision、环境级 Snapshot、ETag、更新检查和 Last-Known-Good。

## 1. 配置模型

| 字段 | 含义 |
| --- | --- |
| Value Kind | Boolean、Integer (`long`)、Double、String、JSON |
| Visibility | Client 或 Server |
| Schema JSON | 可选值约束，尤其用于 JSON |
| Default Value | 没有 Targeting 命中时的值 |
| Targeting Rules | 按顺序把 Segment 映射到特定值 |
| Draft / Published | 编辑态与运行态分离 |
| Snapshot Version | Environment 当前已发布配置集合版本 |

`Server` Visibility 只表示不会出现在普通 Client Snapshot；它仍不是 Secret Vault。密码、私钥、Token
和数据库连接串必须放 Secret Manager。

## 2. Web 工作流

路由：`/config`

1. 创建稳定 Key，选择 Value Kind 和 Visibility。
2. 配置默认值、Schema 与可选 Segment Targeted Values。
3. Validate Draft 并查看 Diff/Changed Paths。
4. 使用 Preview Config Value 测试代表性 Context。
5. Publish；服务端生成新的 Revision 和 Environment Snapshot。
6. 查看 Revision/Snapshot History，必要时 Rollback。
7. Archive/Restore 不再使用或重新启用的 Entry。

运行面只返回 Published、Active 且 Visibility 允许的 Entry。Draft 修改在 Publish 前不会泄漏到客户端。

## 3. Snapshot 与 ETag

- Snapshot 是一个 Environment 在某次发布后的已发布 Entry 集合。
- 客户端按 Scope、Visibility 和 Targeting Context 获取最终 Effective Values。
- ETag 也包含 Context 影响；不同用户的 Targeted Value 可能产生不同 ETag。
- SDK 发送 `If-None-Match`，服务端未变化时返回 Not Modified。
- `CheckForUpdatesAsync` 只比较 Snapshot Version；Changed 后再拉取完整 Snapshot。
- `SnapshotUpdated` 在缓存版本真正变化后通知应用。

## 4. C# 接入

```csharp
using Asterloom.Sdk.Config;

var scope = new AsterloomConfigScope(tenantId, applicationId, environmentId);
using var config = new AsterloomConfigClient(
    transport.HttpClient,
    new AsterloomConfigClientOptions
    {
        Scope = scope,
        CacheDuration = TimeSpan.FromSeconds(30),
        LastKnownGoodDuration = TimeSpan.FromHours(24),
    });

var context = AsterloomConfigContext.Create(
    scope,
    targetingKey: installationId,
    userId: userId,
    clientVersion: appVersion,
    platform: "win-x64",
    region: "CN");

string endpoint = await config.GetStringAsync(
    "checkout.endpoint",
    defaultValue: "https://fallback.example/",
    context,
    cancellationToken);

var status = await config.CheckForUpdatesAsync(
    context,
    knownSnapshotVersion,
    cancellationToken);
```

SDK 提供 `GetBooleanAsync`、`GetInt64Async`、`GetDoubleAsync`、`GetStringAsync` 和 `GetJsonAsync<T>`。
Key 不存在时返回调用者 Default；已存在但类型不匹配时抛出 `AsterloomConfigValueTypeException`，用于发现
契约错误。

## 5. 缓存与 Last-Known-Good

- 默认 Cache 30 秒、Last-Known-Good 24 小时，最大可配置 30 天。
- 网络/JSON/Timeout 故障时，仍在 LKG 窗口内的 Snapshot 会以 `IsLastKnownGood=true` 返回。
- 没有可用 LKG 时抛出 `AsterloomConfigUnavailableException`；应用应有安全本地 Default。
- 默认内存 Cache 进程重启后丢失；需要离线启动的桌面应用应实现加密、原子写入、带完整性校验的
  `IAsterloomConfigSnapshotCache`。

## 6. Server Values

后台服务确实需要 Server Visibility 时，必须同时：

1. 为身份授予 `config.snapshot.server.read`。
2. 在 `AsterloomConfigClientOptions` 显式设置 `AllowServerValues = true`。
3. 调用 `GetSnapshotAsync(..., includeServerValues: true)`。

不要给桌面/浏览器身份该权限。即使是 Server Value，也不要存储真正的 Secret。

## 7. 权限

- 管理：`config.entry.read/create/update/validate/publish/rollback/archive/restore`
- 检查：`config.diff.read`、`config.revision.read`、`config.preview.execute`
- 运行：`config.snapshot.read`、`config.snapshot.server.read`、`config.update.check`
- 历史：`config.snapshot.history.read`

## 8. 实施规则

- Key 和 Value Kind 发布后视为 API 契约，不要原地改变类型。
- 所有读取提供保守 Default，并区分“缺失”和“类型错误”。
- Production Publish 前执行 Validate、Diff、Preview 和代表性 Context 测试。
- 配置体积保持小；大文件和模型使用 File Storage。
- 不轮询过快，使用 Cache、ETag 和合理的更新间隔。
- Rollback 生成新的 Snapshot；客户端仍按缓存周期或更新检查感知。

## 9. 相关实现

- Runtime Protocol：[config.proto](../../Proto/Asterloom/config/v1/config.proto)
- Admin Protocol：[config_admin.proto](../../Proto/Asterloom/config/v1/config_admin.proto)
- Types：[config_types.proto](../../Proto/Asterloom/config/v1/config_types.proto)
- SDK：[AsterloomConfigClient.cs](../../Backend/Asterloom.Sdk.Config/AsterloomConfigClient.cs)
- 评估服务：[ConfigEvaluationService.cs](../../Backend/Asterloom.Module.Config/ConfigEvaluationService.cs)
- 管理服务：[ConfigManagementService.cs](../../Backend/Asterloom.Module.Config/ConfigManagementService.cs)
- Web：[config-workspace.tsx](../../Frontend/features/config/config-workspace.tsx)
