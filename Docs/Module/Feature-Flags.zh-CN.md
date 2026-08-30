# Feature Flag：功能开关与 Variant

[简体中文](Feature-Flags.zh-CN.md) | [English](Feature-Flags.md) | [模块索引](README.zh-CN.md)

Feature 模块用于在不重新发布客户端的情况下启用、关闭或分配功能 Variant。它兼容 OpenFeature
C# Provider，并复用 Targeting Segment 与十万分稳定分桶。

## 1. 数据模型

每个 Flag 固定一种 Value Kind：Boolean、String、Integer、Double 或 Object。Definition 包含：

- `Enabled` 与 `DefaultVariantKey`
- 一组类型一致的 Variant
- 可选 Prerequisite Flag + Expected Variant
- 按顺序执行的 Segment Targeting Rule
- 可选 Bucketing Allocation 与稳定 Salt
- Draft Revision 和 Published Revision

运行面只读取 Published Definition；编辑 Draft 不会立即影响用户。

## 2. Web 工作流

路由：`/features`

1. 选择 Tenant、Application、Environment。
2. 创建稳定 Flag Key，选定 Value Kind 和初始 Variant。
3. 编辑 Draft：默认 Variant、Prerequisite、Segment Rule、百分比分配。
4. Validate Draft，修复类型、引用、区间或循环依赖错误。
5. 用 Simulate 输入真实代表性的 Evaluation Context。
6. Publish；运行面开始读取新的不可变 Revision。
7. 查看 Revision，必要时 Rollback、Archive 或 Restore。

Rollback 会以历史 Definition 生成新的已发布 Revision，不会删除后续审计历史。

## 3. 评估顺序

```text
Active + Published
  → Enabled?
  → Prerequisites
  → ordered Segment rules
  → stable bucket allocation
  → default variant
```

结果包含 Value、Variant Key、Revision、Reason、Trace、Bucket 和 Bucketing Version，便于模拟和问题定位。
归档、未发布、类型不匹配或上下文无效时，SDK 返回调用者提供的安全 Default，并携带 OpenFeature
Error 信息。

## 4. C# / OpenFeature 接入

```csharp
using Asterloom.Sdk.Feature;
using OpenFeature.Model;

var provider = new AsterloomFeatureProvider(
    transport.CallInvoker,
    new AsterloomFeatureProviderOptions
    {
        Scope = new AsterloomFeatureScope(tenantId, applicationId, environmentId),
        CacheDuration = TimeSpan.FromSeconds(30),
        LastKnownGoodDuration = TimeSpan.FromHours(24),
    });

var context = EvaluationContext.Builder()
    .SetTargetingKey(installationId)
    .Set("userId", userId)
    .Set("clientVersion", appVersion)
    .Set("platform", "win-x64")
    .Set("region", "CN")
    .Build();

var result = await provider.ResolveBooleanValueAsync(
    "new-checkout",
    defaultValue: false,
    context,
    cancellationToken);

bool enabled = result.Value;
```

Provider 还实现 String、Integer、Double 和 Structure Resolver。`targetingKey` 必填；Context 只接受
String/Boolean/Number 自定义属性。

## 5. 缓存与故障行为

- 默认成功结果 Cache 30 秒，按 Flag、类型和 Context 隔离。
- RPC 暂时失败时，可以在默认 24 小时 Last-Known-Good 窗口内返回最近成功结果。
- 没有可用缓存时返回代码提供的 Default，不应让开关服务故障导致应用无法启动。
- 高风险 Kill Switch 的缓存时长应按业务 RTO 缩短，并验证离线语义。
- 发布后需要立即刷新时可调用 `ClearCache()`，但不要在每次评估前清空。

## 6. 管理自动化

`AsterloomFeatureAdminClient` 支持 List/Get/Create、Update Draft、Validate、Publish、Revision、Rollback、
Archive/Restore 和 Simulation。业务应用通常只需要 `AsterloomFeatureProvider`；不要给终端应用发布权限。

## 7. 权限

- `feature.flag.read/create/update/validate/publish/rollback/archive/restore/evaluate`
- `feature.revision.read`
- `feature.simulation.execute`

Feature Flag 不是安全边界。即使 UI Flag 为 false，服务端敏感操作仍必须检查 Authorization。

## 8. 实施规则

- Flag Key 创建后保持稳定，代码中集中定义，删除代码前先停止流量并观察。
- 所有调用都提供保守 Default。
- Variant 值必须保持同一 Value Kind；不要改变已发布 Flag 的类型契约。
- Prerequisite 图保持无环，并避免过深依赖。
- Targeting Context 不放 PII/Token；记录 Analytics 时只发送必要的 Flag/Variant/Revision。
- 发布前覆盖 Disabled、Prerequisite Fail、Segment Match、Bucket 和 Default 路径。

## 9. 相关实现

- Runtime Protocol：[feature.proto](../../Proto/Asterloom/feature/v1/feature.proto)
- Admin Protocol：[feature_admin.proto](../../Proto/Asterloom/feature/v1/feature_admin.proto)
- Types：[feature_types.proto](../../Proto/Asterloom/feature/v1/feature_types.proto)
- 评估服务：[FeatureEvaluationService.cs](../../Backend/Asterloom.Module.Feature/FeatureEvaluationService.cs)
- OpenFeature Provider：[AsterloomFeatureProvider.cs](../../Backend/Asterloom.Sdk.Feature/AsterloomFeatureProvider.cs)
- Admin SDK：[AsterloomFeatureAdminClient.cs](../../Backend/Asterloom.Sdk.Feature/AsterloomFeatureAdminClient.cs)
- Web：[feature-workspace.tsx](../../Frontend/features/feature/feature-workspace.tsx)
- Targeting：[Targeting-Rollout.zh-CN.md](Targeting-Rollout.zh-CN.md)
