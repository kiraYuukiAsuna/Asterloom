# Asterloom 功能使用指南

[简体中文](Feature-Guide.zh-CN.md) | [English](Feature-Guide.md)

本文介绍如何通过 Web 管理后台操作 Asterloom 的全部能力，以及 .NET
应用如何使用运行时 C# SDK。内容以当前仓库已经实现和验证的功能为准，不包含尚未实现的
多语言 SDK。需要某一能力的边界、权限、完整工作流与实现链接时，使用
[模块使用文档](Module/README.zh-CN.md)。

## 1. 接入方式

| 使用方 | 推荐链路 | 用途 |
| --- | --- | --- |
| 管理员 | 浏览器 → Next.js Web Console/BFF → HTTP/JSON | 配置、操作和检查全部管理能力。 |
| .NET 桌面应用 | C# SDK → 原生 gRPC 或文件传输端点 | 登录、评估 Flag/Config、检查更新、上报 Analytics 和传输文件。 |
| .NET 后端服务 | Client Credentials → 带认证的 C# SDK Transport | 服务间鉴权和运行时调用。 |
| 第三方 HTTP 客户端 | 生成的 OpenAPI Client 或 JSON Transcoding 路由 | 通过 HTTP/JSON 使用同一份 Protobuf 契约。 |

`Proto/Asterloom` 是 API 契约唯一来源。自定义业务 RPC 都通过
`google.api.http` 开启 JSON Transcoding，因此原生 gRPC 与 HTTP/JSON
调用的是同一套服务端实现。浏览器通常应调用 Next.js BFF 下的
`/api/asterloom/*`，不能直接获得 Asterloom Access Token。

## 2. 作用域模型与推荐配置顺序

多数运行时资源属于以下层级：

```text
Tenant（租户）
  └─ Application（应用）
       └─ Environment（环境）
            ├─ Targeting Segment
            ├─ Feature Flag
            ├─ Dynamic Config
            ├─ Desktop Release
            ├─ Analytics
            └─ Telemetry
```

SDK 配置应保存稳定的资源 ID。Slug 用于后台展示和搜索，API 返回的 ID 才是权威作用域值。

新产品建议按以下顺序接入：

1. 创建 Tenant、Application 和 Environment。
2. 邀请管理员，并注册交互式或服务型 OIDC Client。
3. 创建角色、角色绑定以及必要的 Allow/Deny 策略。
4. 创建可以复用的 Targeting Segment。
5. 发布 Feature Flag 和 Dynamic Config。
6. 创建 Storage Bucket、Release Channel 和签名公钥。
7. 创建 Analytics Schema/Write Key 以及 Telemetry Source/Settings。
8. 分别使用模拟器、Operations、Audit 和参考应用 Doctor 完成验收。

## 3. 启动本地环境

使用容器启动需要 Docker 与 Docker Compose；直接运行源码需要 .NET SDK 10.0.400
和 Node.js 24。

```powershell
Copy-Item Deploy/.env.example .env
docker compose up --build
```

打开 `http://localhost:60000`，使用 `.env` 中的本地管理员登录。仓库示例默认值为
`admin@asterloom.local` / `Asterloom-Local-Admin!2026`，只能用于本地开发，禁止复用到生产。

本地端点如下：

| 端点 | 地址 |
| --- | --- |
| Web Console/BFF | `http://localhost:60000` |
| Server HTTP/JSON 与 Passport | `http://localhost:60001` |
| Server 原生 gRPC | `http://localhost:60002` |
| MinIO S3 API | `http://localhost:60003` |
| Reference HTTP / gRPC | `http://localhost:60004` / `http://localhost:60005` |
| PostgreSQL、Redis、MinIO Console、OTLP、Collector Health | 不映射到宿主机，仅 Compose 网络可达 |

## 4. Web 管理后台使用方法

下面列出的每个管理操作都有对应的 Admin RPC，并由 API/UI 覆盖检查和 Playwright
完整旅程固定。

### 4.1 平台资源

路由：`/tenants`

1. 使用唯一 Slug 和显示名称创建 Tenant。
2. 选中 Tenant 后创建 Application。
3. 选中 Application 后创建 Development、Staging 或 Production Environment；敏感环境可标记为 Protected。
4. 按主体和角色添加或移除 Tenant Membership。
5. 使用 Edit、Archive、Restore、搜索和分页管理资源生命周期。

Archive 是默认的可恢复移除方式。父级被归档或保护后，可能会阻止对子级资源的写操作。

### 4.2 Identity / Passport

路由：`/identity/users`

- **用户：**创建或邀请全局账号、重发邀请、编辑资料、设置 Passport 角色、重置密码、暂停/恢复、归档/还原。
- **会话：**查看用户会话、撤销单个会话或撤销该用户全部会话。
- **应用成员关系：**按应用添加、恢复、筛选或移除全局账号。
- **OIDC Client：**创建 Native Public Client、Web/Service Confidential Client，绑定 Platform Application，配置注册/自动加入、Redirect URI、Grant 与 Scope，轮换 Secret，删除 Client。
- **OIDC Scope：**创建、修改和删除 Passport 对外提供的 Scope。

交互式桌面/管理 Web 客户端使用 Authorization Code + S256 PKCE，后端服务使用 Client Credentials。
Password Flow 与 Implicit Flow 均禁用；业务应用不得收集 Passport 密码来直接换取 Token。完整流程见
[业务应用统一账号接入](Module/Identity-Business-Integration.zh-CN.md)。新生成的 Client Secret 应立即保存到 Secret Manager。

### 4.3 Authorization

路由：`/authorization/roles`

1. 查看 Permission Catalog。
2. 创建 Role 并分配 Permission Key。
3. 将 Role 绑定到 Actor，可选择 Global、Tenant、Application 或 Environment 作用域。
4. 角色绑定不足以表达规则时，再创建明确的 Policy Rule；发生冲突时按策略模型处理 Deny 优先级。
5. 查看 Policy Revision，并在敏感变更上线前使用 Simulator 验证。
6. 业务服务在真正执行操作前调用运行时 Permission Check 获取最终判断。

前端隐藏按钮只能改善体验，不能替代服务端权限检查。

### 4.4 Targeting 与灰度分桶

路由：`/targeting/segments`

- 查看平台支持的属性与运算符。
- 使用类型化规则创建 Segment，并可更新、归档和还原。
- 输入 Targeting Key、用户 ID、客户端版本、平台、区域、语言等属性进行模拟。
- 使用 Bucket Preview 检查百分比灰度是否稳定。

Feature、Config 和 Release 共用同一套 Targeting Context 与稳定分桶算法。需要同一个主体在
多次请求中稳定命中时，应始终使用相同 Targeting Key。

### 4.5 Feature Flag

路由：`/features`

1. 选择 Tenant、Application 和 Environment。
2. 创建类型化 Flag，配置 Variant 和默认值。
3. 在 Draft 中配置前置条件、Targeting Rule 和百分比分配。
4. Validate Draft 后再 Publish。
5. 使用 Simulate 查看可解释的控制面结果，使用 Evaluate 验证运行时结果。
6. 查看 Revision，回滚到历史版本，或 Archive/Restore Flag。

运行时只读取已经发布的 Revision。客户端必须提供安全默认值；Targeting 属性是业务数据，不能作为安全鉴权边界。

### 4.6 Dynamic Config

路由：`/config`

1. 创建 Boolean、Integer、Double、String 或 JSON 配置项。
2. 编辑 Draft 默认值及可选的定向值。
3. Validate 并查看 Draft Diff。
4. 使用代表性的 Targeting Context 预览最终有效值。
5. Publish 后查看 Revision/Snapshot、检查更新或执行 Rollback。
6. 不再使用的配置项可以 Archive/Restore。

SDK 的缓存和 Last-Known-Good 能在临时网络故障时继续返回最近一次验证成功的快照。密码、Token
等机密数据必须存放在 Secret Manager，不能放入 Dynamic Config。

### 4.7 桌面发布与自动更新

路由：`/channels`、`/artifacts`、`/releases`

这一能力还涉及 RID、Velopack 包结构、首次安装程序、外部签名和上传协议。实施前请先阅读
[Asterloom 桌面自动更新指南](Module/Desktop-Updates.zh-CN.md)，不要把普通发布目录或任意 ZIP 当作
可安装 Artifact。

1. 创建 `stable`、`beta`、`canary` 等 Channel。
2. 在 Asterloom 外部生成 RSA 签名密钥；后台只登记公钥，私钥留在受控构建/签名系统。
3. 上传 Artifact，核对 SHA-256，按约定使用 RSA-PSS 对摘要签名并完成上传票据。
4. 创建 Release Draft，关联已验证 Artifact，设置 Minimum Version 和 Rollout Basis Points（`100000` 表示 100%）。
5. Validate Manifest，对 Manifest 摘要签名后 Publish。
6. 使用更新模拟器验证，然后按需 Pause、Promote 或 Rollback。

C# 客户端会依次验证可信公钥指纹、Manifest 签名、Artifact 签名和 SHA-256，验证通过后才提供文件。
需要完成桌面程序替换和重启时，应使用 Velopack 打包，并把
`AsterloomVelopackUpdateSource` 交给 Velopack `UpdateManager`。

### 4.8 Analytics

路由：`/analytics/schemas`、`/analytics/explorer`

1. 创建事件 JSON Schema；敏感属性用 `x-asterloom-sensitive: true` 标记。
2. 设置保留时间并为生产者创建 Write Key。
3. 创建或轮换后立即复制 Write Key Secret，不再使用时撤销。
4. 使用 Analytics SDK 批量上报事件。
5. 在 Explorer 中查看脱敏事件、执行聚合查询并导出筛选后的 CSV。

Analytics 表示产品行为和业务结果。即使配置了脱敏，也不应上报密码、Token、银行卡数据或无限制 PII。

### 4.9 Telemetry

路由：`/telemetry/sources`、`/telemetry/health`

- 注册服务 Source 和 Resource Attribute。
- 配置 Trace Sampling、OTLP gRPC/HTTP Protobuf Exporter Endpoint 及诊断系统基础地址。
- 检查 Collector Health 和近期错误。
- 根据 Trace ID 生成诊断跳转链接。

Telemetry 用于 Trace、Metric、Log、Exception 与技术健康；它与产品行为 Analytics 相互独立。

### 4.10 文件存储

路由：`/storage/buckets`、`/storage/objects`

Storage 侧边栏默认进入 `/storage/objects`；创建入口位于工作区顶部的 **Buckets** 页签，页面
下方有 **Create bucket** 卡片。管理员通常先在 Web 创建逻辑 Bucket，业务应用再使用配置好的
`BucketId` 通过 SDK/API 上传和下载；Web 也支持人工上传、下载、复制和删除。

1. 创建并配置逻辑 Bucket。
2. 创建 Upload Session，使用短时 Signed URL 和返回的全部 Required Header 上传字节，然后 Complete Session。
3. 查看对象 Metadata 和服务端验证的 SHA-256。
4. 生成短时下载地址、复制对象、修改 Custom Metadata 或删除对象。
5. 根据生命周期 Archive/Restore Bucket。

对象传输地址可能指向 S3 Origin，而不是 Asterloom API Origin。禁止把 Asterloom Bearer Token
附加到该地址，只能使用 Transfer Ticket 返回的签名 Header。

资源模型、完整三阶段传输协议、权限与 SDK 覆盖边界见
[Asterloom 文件存储指南](Module/File-Storage.zh-CN.md)。

### 4.11 Operations、Audit 与主题

- `/operations/apis`：查看 gRPC/HTTP API 目录并下载 OpenAPI。
- `/operations/health`：检查平台与依赖健康状态。
- `/audit`：搜索、筛选、查看详情和关联请求，并导出不可变审计事件。
- 主题开关支持 Light、Dark、System，并会保存用户选择。

Operations 用于部署和依赖健康，Audit 用于回答“谁在什么时候修改了什么”；二者都不能替代
Telemetry 的 Trace 和日志。

## 5. C# SDK 接入

当前 SDK 以仓库项目形式提供。发布到 NuGet Feed 之前，应用只引用自己需要的能力。例如在仓库根目录执行：

```powershell
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Identity\Asterloom.Sdk.Identity.csproj
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Rpc\Asterloom.Sdk.Rpc.csproj
dotnet add .\path\to\MyApp.csproj reference .\Backend\Asterloom.Sdk.Feature\Asterloom.Sdk.Feature.csproj
```

完整可执行示例位于 `Backend/Samples/Asterloom.ReferenceApp.Client`。

### 5.1 服务登录与统一认证 Transport

```csharp
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = builder.Configuration["Asterloom:ClientId"]!;
    options.ClientSecret = builder.Configuration["Asterloom:ClientSecret"]!;
    options.EnableServiceCredentials = true;
});

using var host = builder.Build();
await host.StartAsync();
var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
await identity.GetServiceAccessTokenAsync();

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);
```

gRPC SDK 使用 `transport.CallInvoker`，HTTP/传输类 SDK 使用
`transport.HttpClient`。Bearer Handler 只向配置的 Asterloom Origin 发送 Token，并会跳过
S3 Signed URL。

### 5.2 交互式 Passport 登录

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = "my-desktop-client";
    options.EnableInteractiveAuthentication = true;
    options.RequestRefreshTokens = true;
});

var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
var tokens = await identity.SignInAsync(cancellationToken: cancellationToken);
var accessToken = await identity.GetAccessTokenAsync(cancellationToken);
await identity.SignOutAsync(cancellationToken);
```

SDK 会打开系统浏览器并执行 Authorization Code + PKCE。生产桌面应用应将默认内存 Token
Store 替换为操作系统保护的实现。

### 5.3 权限检查

```csharp
using Asterloom.Sdk.Authorization;

var authorization = new AsterloomAuthorizationClient(transport.CallInvoker);
var decision = await authorization.CheckPermissionAsync(
    "feature.flag.evaluate",
    new AsterloomAuthorizationScope(tenantId, applicationId, environmentId),
    cancellationToken);

if (!decision.Allowed)
{
    throw new UnauthorizedAccessException(decision.Reason);
}
```

### 5.4 Targeting 与 Feature Flag

```csharp
using Asterloom.Sdk.Feature;
using OpenFeature.Model;

var provider = new AsterloomFeatureProvider(
    transport.CallInvoker,
    new AsterloomFeatureProviderOptions
    {
        Scope = new AsterloomFeatureScope(tenantId, applicationId, environmentId),
    });

var context = EvaluationContext.Builder()
    .SetTargetingKey(userId)
    .Set("region", "CN")
    .Set("clientVersion", appVersion)
    .Build();

var result = await provider.ResolveBooleanValueAsync(
    "new-checkout",
    defaultValue: false,
    context,
    cancellationToken);

bool enabled = result.Value;
```

管理自动化可使用 `AsterloomTargetingAdminClient`。只有明确需要本地稳定分桶时，才直接调用
`AsterloomTargetingEvaluator.ComputeBucket`。

### 5.5 Dynamic Config

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
    targetingKey: userId,
    clientVersion: appVersion,
    platform: "win-x64",
    region: "CN");

string endpoint = await config.GetStringAsync(
    "checkout.endpoint",
    "https://fallback.example/",
    context,
    cancellationToken);
```

SDK 提供 Boolean、`long`、`double`、String 和 JSON 类型 Getter。需要监听变化时，可订阅
`SnapshotUpdated` 或调用 `CheckForUpdatesAsync`。

### 5.6 桌面更新

```csharp
using Asterloom.Sdk.Release;

var scope = new AsterloomReleaseScope(tenantId, applicationId, environmentId);
using var releases = new AsterloomReleaseClient(
    transport.HttpClient,
    new AsterloomReleaseClientOptions
    {
        Scope = scope,
        TargetRuntimeId = "win-x64",
        PackageId = "my-desktop-app",
        TrustedPublicKeysByFingerprint = trustedReleaseKeys,
    });

var decision = await releases.CheckForUpdateAsync(
    "stable",
    currentVersion,
    AsterloomReleaseContext.Create(
        scope,
        targetingKey: installationId,
        clientVersion: currentVersion,
        platform: "win-x64"),
    cancellationToken);

if (decision.UpdateAvailable)
{
    await releases.DownloadToFileAsync(
        decision,
        destinationPath,
        cancellationToken: cancellationToken);
}
```

不要绕过 `AsterloomReleaseClient` 自己下载 Artifact URL，否则会失去签名与哈希验证。
有关 `win-x64` 等 RID、Velopack 打包、Artifact/Manifest 签名和发布顺序，请阅读
[Asterloom 桌面自动更新指南](Module/Desktop-Updates.zh-CN.md)。

### 5.7 Analytics

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

await analytics.TrackAsync(
    "checkout.completed",
    new { orderId, amount },
    new AsterloomAnalyticsIdentity(
        ActorId: userId,
        SessionId: sessionId),
    cancellationToken: cancellationToken);

var flush = await analytics.FlushAsync(cancellationToken);
```

程序正常退出时应调用 `FlushAsync`；`DisposeAsync` 也会尝试在限定时间内完成最后一次 Flush。

### 5.8 Telemetry

```csharp
using Asterloom.Sdk.Telemetry;

var telemetry = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    serviceName: "my-company.checkout",
    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString());

telemetry.TenantId = tenantId.ToString("D");
telemetry.ApplicationId = applicationId.ToString("D");
telemetry.EnvironmentId = environmentId.ToString("D");
telemetry.ActivitySourceNames.Add("MyCompany.Checkout");
telemetry.MeterNames.Add("MyCompany.Checkout");

builder.Services.AddAsterloomTelemetry(telemetry);
builder.Logging.AddAsterloomTelemetryLogging(telemetry);
```

通过 `OTEL_EXPORTER_OTLP_ENDPOINT` 配置 Collector。Metric Label 必须保持低基数；请求或用户
细节应放入受控 Trace/Log，而不是默认 Metric Tag。

### 5.9 Storage

```csharp
using System.Security.Cryptography;
using Asterloom.Sdk.Storage;

using var storage = new AsterloomStorageClient(
    transport.HttpClient,
    new AsterloomStorageClientOptions
    {
        Scope = new AsterloomStorageScope(tenantId),
    });

byte[] bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

var stored = await storage.UploadAsync(
    new AsterloomStorageUploadRequest(
        bucketId,
        ObjectKey: $"documents/{Path.GetFileName(sourcePath)}",
        FileName: Path.GetFileName(sourcePath),
        ContentType: "application/octet-stream",
        SizeBytes: bytes.LongLength,
        Sha256: sha256,
        ApplicationId: applicationId,
        EnvironmentId: environmentId),
    new MemoryStream(bytes, writable: false),
    cancellationToken);

await using var destination = File.Create(destinationPath);
await storage.DownloadToAsync(stored, destination, cancellationToken: cancellationToken);
```

当前 `AsterloomStorageClient` 封装对象上传和下载；Bucket 管理以及 Object List、Metadata、Copy、
Delete 已由 gRPC/JSON API 和 Web 覆盖，需要时使用生成的 API Client。完整说明见
[Asterloom 文件存储指南](Module/File-Storage.zh-CN.md)。

### 5.10 原生 gRPC、HTTP/JSON 与 Persistence

应用自己的后台可以用一份 Protobuf 同时暴露两种传输：

```csharp
builder.Services.AddGrpc().AddJsonTranscoding();

var app = builder.Build();
app.MapGrpcService<MyGrpcService>();
```

每个自定义 RPC 都添加 `google.api.http`。NET 客户端调用生成的 gRPC Client，浏览器和普通
HTTP Client 调用对应 JSON 路由。不要再实现一套包含重复业务逻辑的 MVC Controller。

Persistence 不是通用远程数据库 API。Asterloom 模块把自身数据写入各自 PostgreSQL Schema；
你的服务使用 Npgsql 持久化自己的业务数据，例如：

```csharp
builder.Services.AddSingleton(_ =>
    Npgsql.NpgsqlDataSource.Create(
        builder.Configuration.GetConnectionString("Application")!));
```

应用表应放在应用自己拥有的 Schema 中，不能跨边界直接读取 Asterloom 模块表。

## 6. 全能力参考应用

参考应用是上面 SDK 用法的可执行示例：

```powershell
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- provision
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- doctor
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- login
$env:ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD = "Use-A-Strong-Test-Password!2026"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- account-demo user@example.com "Example User"
```

- `provision` 创建一套隔离的全能力资源。
- `doctor` 分别验证 Identity、Authorization、Targeting、Feature/Rollout、Config、Release、Analytics、Telemetry、原生 gRPC、JSON Transcoding、Storage、PostgreSQL Persistence 和 Operations/OpenAPI。
- `login` 验证交互式 Passport Authorization Code + PKCE。
- `account-demo` 验证全局账号注册、确认和应用成员关系；`login` 完成交互登录并验证业务 API Bearer Token。

环境变量、生产 Compose 命令和诊断成功条件见
[Reference-Application.md](Reference-Application.md)。

## 7. 验证命令

```powershell
dotnet restore Backend/Asterloom.sln
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln

Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run test:e2e
```

生产 E2E 使用 `npm run test:e2e:production`，凭据只能通过 `ASTERLOOM_E2E_*`
环境变量传入。没有独立命名和清理策略时，不应对生产环境执行会修改数据的 E2E。

## 8. 生产安全检查表

- 使用 HTTPS 和固定 OIDC Issuer。
- 签名私钥、Client Secret、Write Key、Redis 凭据、Session Encryption Key、数据库和 S3 凭据全部进入 Secret Manager。
- 生产 BFF 使用 Redis Session；`memory` 只能用于开发。
- 浏览器 Cookie 必须是 Secure、HttpOnly、正确设置 SameSite，并且只包含不透明 Session ID。
- Server 启动前显式执行数据库迁移。
- 备份 PostgreSQL 与对象存储，并实际演练恢复。
- Transfer URL 使用短有效期、最小权限、限流和适当审计保留期。
- 部署后验证 `/health/live`、`/health/ready`、`/health/startup`、Operations 和参考应用 `doctor`。
