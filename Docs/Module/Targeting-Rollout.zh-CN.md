# Targeting 与稳定灰度发布

[简体中文](Targeting-Rollout.zh-CN.md) | [English](Targeting-Rollout.md) | [模块索引](README.zh-CN.md)

Targeting 是 Feature、Dynamic Config 和 Desktop Release 共用的受众规则与确定性分桶引擎。Segment
回答“这个上下文是否属于某个受众”，Rollout 回答“这个稳定主体是否落在某个百分比区间”。

## 1. Evaluation Context

| 字段 | 用途 |
| --- | --- |
| `targetingKey` | 必填稳定主体键；决定灰度桶 |
| `userId` | 可选账号 ID |
| `applicationId` / `environmentId` | 资源作用域 |
| `clientVersion` | Semantic Version 条件 |
| `platform` | Targeting 属性，例如 `win-x64`；不负责选择 Release Artifact |
| `region` / `language` | 区域和语言规则 |
| Custom Attributes | 最多 64 个 Text/Truth/Numeric 属性 |

`targetingKey` 应选稳定且无敏感含义的 Installation ID、User ID 或服务实例键。不要每次启动随机生成，
否则灰度成员会漂移。Email、姓名、手机号、原始 Device ID 等 PII 风格的自定义属性名会被校验拒绝；
服务端无法判断任意属性值中是否夹带 PII，调用方仍必须主动最小化和去敏。

## 2. Segment 规则

路由：`/targeting/segments`

一个 Segment 在一个 Environment 内有稳定 Key、Display Name 和一条平面 Rule。Rule 使用 `ALL` 或
`ANY` 组合 1–50 个 Condition，并按声明顺序短路。

支持的主要 Operator：

- Text：Equals、NotEquals、OneOf、Contains、StartsWith、EndsWith。
- Numeric：Equals、比较运算。
- Boolean：Equals、NotEquals。
- Presence：Exists、NotExists。
- Version：SemanticVersionEquals/GreaterThan/LessThan。

Web 支持 Attribute/Operator Catalog、Segment List/Get/Create/Update/Archive/Restore 和 Simulation。
模拟结果返回每个 Condition 的 Matched、Missing Attribute、Type Mismatch 等原因。

## 3. Bucketing v1

```text
material = UTF8("v1" + NUL + namespace + NUL + salt + NUL + targetingKey)
hash     = SHA-256(material)
value    = hash 前 8 字节按无符号大端 UInt64
bucket   = value mod 100000
```

- 总桶数固定为 `100000`；1% = `1000`，12.5% = `12500`。
- 分配区间为左闭右开 `[start, end)`，不能重叠。
- Namespace 包含资源类型、资源 Key 和 Environment，避免不同资源意外共享桶。
- Salt 必须稳定；改变 Salt 会重新洗牌全部用户，应视作显式破坏性变更。
- 相同 Namespace、Salt、Targeting Key 在服务端与 C# SDK 中始终得到相同桶。

## 4. C# 管理与本地预览

```csharp
using Asterloom.Sdk.Targeting;

var admin = new AsterloomTargetingAdminClient(transport.CallInvoker);
var scope = new AsterloomTargetingScope(tenantId, applicationId, environmentId);
var catalog = await admin.ListTargetingAttributesAsync(cancellationToken);
var segments = await admin.ListSegmentsAsync(scope, cancellationToken: cancellationToken);

uint bucket = AsterloomTargetingEvaluator.ComputeBucket(
    resourceType: "feature",
    resourceKey: "checkout-v2",
    environmentId: environmentId,
    salt: "stable-salt",
    targetingKey: installationId);
```

管理操作使用 `AsterloomTargetingAdminClient`；Feature/Config/Release 的正常运行评估应通过对应 SDK，
不要在每个应用里复制一套规则和 Hash 算法。

## 5. 与其他模块的关系

- Feature Flag：Segment 决定 Rule 是否命中，Bucketing 选择 Variant。
- Dynamic Config：Segment 决定是否使用 Targeted Value。
- Desktop Release：Segment 限制受众，Rollout Basis Points 控制发布比例。
- Analytics：可以记录最终 Variant/Reason，但不要上报完整 Context。

Segment 归档后依赖它的草稿应在发布校验中失败或不再命中。生产变更前先在各消费模块的 Simulator
中覆盖匹配、不匹配、缺失属性和桶边界。

## 6. 权限

- `targeting.attribute.read`
- `targeting.segment.read/create/update/archive/restore`
- `targeting.simulation.execute`

运行评估权限属于消费模块，例如 `feature.flag.evaluate` 或 `release.update.check`。

## 7. 上线检查

- [ ] `targetingKey` 来源稳定，并在安装/账号生命周期内持久化。
- [ ] Context 不包含 PII、Token 或无限制高基数字段。
- [ ] Semantic Version 字段使用合法版本字符串。
- [ ] Rollout 从小比例逐步 Promote，并结合 Telemetry/Analytics 观察。
- [ ] Web Simulation 与 C# Golden Vector 结果一致。
- [ ] 不在 TypeScript 或业务客户端复制独立 Bucketing 实现。

## 8. 相关实现

- Admin Protocol：[targeting_admin.proto](../../Proto/Asterloom/targeting/v1/targeting_admin.proto)
- Types：[targeting_types.proto](../../Proto/Asterloom/targeting/v1/targeting_types.proto)
- 公共算法：[TargetingCore.cs](../../Backend/Asterloom.Shared/Targeting/TargetingCore.cs)
- C# Evaluator：[AsterloomTargetingEvaluator.cs](../../Backend/Asterloom.Sdk.Targeting/AsterloomTargetingEvaluator.cs)
- Admin SDK：[AsterloomTargetingAdminClient.cs](../../Backend/Asterloom.Sdk.Targeting/AsterloomTargetingAdminClient.cs)
- Web：[targeting-workspace.tsx](../../Frontend/features/targeting/targeting-workspace.tsx)
