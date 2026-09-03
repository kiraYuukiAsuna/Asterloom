# Telemetry: Traces, Metrics, Logs, and Diagnostics

[English](Telemetry.md) | [简体中文](Telemetry.zh-CN.md) | [Module index](README.md)

Telemetry uses OpenTelemetry for technical signals: traces, metrics, logs, exceptions, Collector health, and
diagnostic links. Product behavior and conversion events belong in [Analytics](Analytics.md).

## 1. Data flow

```text
.NET application
  → OpenTelemetry instrumentation + Asterloom resource attributes
  → OTLP gRPC :4317 or HTTP/protobuf :4318
  → OpenTelemetry Collector
  → rotating JSON files and optional observability backend
```

The Compose Collector writes traces, metrics, and logs to a persistent named volume as rotating JSON files
(100 MiB per file, 10 backups, seven days) while retaining the `debug` exporter for diagnostics. This supports raw
export without a dashboard; add a query backend only when indexed search, alerts, or Trace Pivot links are needed.

## 2. Web administration

Routes: `/telemetry/sources`, `/telemetry/health`

### Sources

- Register service name, stable key, and expected resource attributes in an Environment.
- List/Get/Create/Update/Archive/Restore sources.
- A Source is a governance record; it does not install application instrumentation automatically.

### Settings and health

- Configure sampling ratio, trace/metric/log switches, exporter endpoint/protocol, and diagnostics base URL.
- Check the Collector health endpoint and latency.
- Filter recent Asterloom Server technical errors by service name or trace ID.
- Generate an external diagnostic link for a trace ID and time range.

Important current boundary: Telemetry Settings stored in the Web console are not pushed automatically into a
running C# SDK. Applications still build `AsterloomTelemetryOptions` from deployment configuration; operations
must keep deployed settings aligned with the control-plane record.

## 3. C# SDK integration

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

Principal environment settings:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
Telemetry__SamplingRatio=0.1
```

The SDK can add ASP.NET Core, HttpClient, and .NET Runtime instrumentation and adds
`asterloom.tenant.id`, `asterloom.application.id`, and `asterloom.environment.id` resources.

## 4. Custom traces and metrics

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

Metric tags must remain low-cardinality. Put user, request, order, and full-URL identifiers in controlled traces
or logs, not metrics. Record exception type and necessary context without defaulting to sensitive payload bodies.

## 5. Sampling and correlation

- `SamplingRatio` is 0–1 and currently uses `TraceIdRatioBasedSampler`.
- Error traces and critical transactions can require Collector tail sampling; SDK head sampling does not guarantee
  retention of every error.
- HTTP/gRPC should propagate W3C Trace Context.
- Asterloom error records carry trace ID, span ID, request ID, and gRPC method for diagnostic navigation.
- Sampling reduces traces and should not suppress low-cardinality health metrics.

## 6. Permissions

- `telemetry.source.read/create/update/archive/restore`
- `telemetry.settings.read/update`
- `telemetry.health.read`
- `telemetry.error.read`
- `telemetry.diagnostic.read`

These management permissions do not authenticate the OTLP receiver. Isolate the production Collector and use
TLS/mTLS or a controlled gateway; never expose unauthenticated 4317/4318 publicly.

## 7. Production checklist

- [ ] The Collector file volume is included in backup/export operations, or a managed exporter is configured.
- [ ] OTLP endpoint, protocol, TLS, and network policy are verified.
- [ ] Service name, version, Environment, and Asterloom scope resources are complete.
- [ ] Sampling balances diagnostic requirements and cost.
- [ ] Metric label cardinality, log redaction, and retention policies are defined.
- [ ] Diagnostics Base URL points to an access-controlled observability backend.
- [ ] Collector health and end-to-end trace, metric, and log smoke tests pass.

## 8. Related implementation

- Admin protocol: [telemetry_admin.proto](../../Proto/Asterloom/telemetry/v1/telemetry_admin.proto)
- Types: [telemetry_types.proto](../../Proto/Asterloom/telemetry/v1/telemetry_types.proto)
- SDK options: [AsterloomTelemetryOptions.cs](../../Backend/Asterloom.Sdk.Telemetry/AsterloomTelemetryOptions.cs)
- SDK registration: [AsterloomTelemetryServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Telemetry/AsterloomTelemetryServiceCollectionExtensions.cs)
- Collector configuration: [otel-collector.yaml](../../Deploy/OpenTelemetry/otel-collector.yaml)
- Web: [telemetry-workspace.tsx](../../Frontend/features/telemetry/telemetry-workspace.tsx)
