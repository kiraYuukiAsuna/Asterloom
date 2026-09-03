# Telemetry：Trace、Metric、Log 与技术诊断

[简体中文](Telemetry.zh-CN.md) | [English](Telemetry.md) | [模块索引](README.zh-CN.md)

Telemetry 基于 OpenTelemetry 收集应用与平台的技术信号：Trace、Metric、Log、Exception、Collector
健康和诊断链接。产品行为和业务转化应使用 [Analytics](Analytics.zh-CN.md)。

## 1. 数据流

```text
.NET application
  → OpenTelemetry instrumentation + Asterloom resource attributes
  → OTLP gRPC :4317 or HTTP/protobuf :4318
  → OpenTelemetry Collector
  → 经过认证的 OTLP/JSON Ingestion API
  → PostgreSQL telemetry.records
```

Compose Collector 将 Trace、Metric、Log 转发到 Asterloom Ingestion API，并规范化保存到 PostgreSQL；
记录默认保留七天，不再创建 JSON 文件。只有需要告警、长期保存或复杂分析时才需要另接可观测性后端。

## 2. Web 管理

路由：`/telemetry/sources`、`/telemetry/signals`、`/telemetry/health`

### Sources

- 为 Environment 注册 Service Name、稳定 Key 和预期 Resource Attributes。
- List/Get/Create/Update/Archive/Restore Source。
- Source 是治理/登记资源，不会自动安装应用侧 Instrumentation。

### Settings 与 Health

- 设置数据库入库 Sampling Ratio、Trace/Metric/Log 开关和 Diagnostics Base URL。
- 检查 Collector Health Endpoint 和延迟。
- 按 Service Name/Trace ID 查看 Asterloom Server 捕获的近期技术错误。
- 按信号类型、Service Name、Trace ID、关键字和时间范围查询 PostgreSQL 中的遥测记录。
- 根据 Trace ID 与时间范围生成外部诊断系统链接。

Web 中的信号开关和 Trace Sampling Ratio 会在入库时执行，但不会自动推送到正在运行的 C# SDK；
应用 SDK 仍从部署配置创建 `AsterloomTelemetryOptions`。

## 3. C# SDK 接入

```csharp
using Asterloom.Sdk.Telemetry;

var telemetry = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    serviceName: "my-company.checkout",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString());

telemetry.EnvironmentName = builder.Environment.EnvironmentName;
telemetry.TenantId = tenantId.ToString("D");
telemetry.ApplicationId = applicationId.ToString("D");
telemetry.EnvironmentId = environmentId.ToString("D");
telemetry.ActivitySourceNames.Add("MyCompany.Checkout");
telemetry.MeterNames.Add("MyCompany.Checkout");

builder.Services.AddAsterloomTelemetry(telemetry);
builder.Logging.AddAsterloomTelemetryLogging(telemetry);
```

主要环境配置：

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
Telemetry__SamplingRatio=0.1
```

SDK 可自动添加 ASP.NET Core、HttpClient、.NET Runtime Instrumentation，并把
`asterloom.tenant.id`、`asterloom.application.id`、`asterloom.environment.id` 加入 Resource。

## 4. 自定义 Trace 与 Metric

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

static readonly ActivitySource Activities = new("MyCompany.Checkout");
static readonly Meter Meter = new("MyCompany.Checkout");
static readonly Counter<long> Completed = Meter.CreateCounter<long>("checkout.completed");

using var activity = Activities.StartActivity("checkout.process");
activity?.SetTag("checkout.payment.provider", providerKey);
Completed.Add(1, new KeyValuePair<string, object?>("result", "success"));
```

Metric Tag 必须低基数；User ID、Request ID、Order ID、完整 URL 等高基数字段放在受控 Trace/Log，
而不是 Metric。异常应记录类型和必要上下文，不要默认记录完整敏感 Payload。

## 5. Sampling 与关联

- `SamplingRatio` 范围 0–1，当前 Trace 使用 `TraceIdRatioBasedSampler`。
- Error Trace、关键事务可能需要 Collector Tail Sampling；当前 SDK 的 Head Sampling 不保证保留所有错误。
- HTTP/gRPC 应传播 W3C Trace Context。
- Asterloom 错误记录包含 Trace ID、Span ID、Request ID 和 gRPC Method，可从 Web 跳转到诊断系统。
- Sampling 会减少 Trace，不应影响低基数业务健康 Metric。

## 6. 权限

- `telemetry.source.read/create/update/archive/restore`
- `telemetry.settings.read/update`
- `telemetry.health.read`
- `telemetry.error.read`
- `telemetry.record.read`
- `telemetry.diagnostic.read`

Collector 到 Server 的入库请求使用 `TELEMETRY_INGESTION_API_KEY`；生产 Collector 仍需网络隔离，
不能把 4317/4318 暴露到公网。

## 7. 上线检查

- [ ] PostgreSQL 已纳入备份并具有足够的遥测容量。
- [ ] OTLP Endpoint、Protocol、TLS 和网络策略已验证。
- [ ] Service Name、Version、Environment 和 Asterloom Scope Resource 完整。
- [ ] Sampling 与成本、故障诊断需求平衡。
- [ ] Metric Label 有基数预算，日志有脱敏和保留策略。
- [ ] Diagnostics Base URL 指向受权限保护的观测后台。
- [ ] Collector Health 和端到端 Trace/Metric/Log 均完成冒烟测试。

## 8. 相关实现

- Admin Protocol：[telemetry_admin.proto](../../Proto/Asterloom/telemetry/v1/telemetry_admin.proto)
- Types：[telemetry_types.proto](../../Proto/Asterloom/telemetry/v1/telemetry_types.proto)
- SDK Options：[AsterloomTelemetryOptions.cs](../../Backend/Asterloom.Sdk.Telemetry/AsterloomTelemetryOptions.cs)
- SDK 注册：[AsterloomTelemetryServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Telemetry/AsterloomTelemetryServiceCollectionExtensions.cs)
- Collector 配置：[otel-collector.yaml](../../Deploy/OpenTelemetry/otel-collector.yaml)
- Web：[telemetry-workspace.tsx](../../Frontend/features/telemetry/telemetry-workspace.tsx)
