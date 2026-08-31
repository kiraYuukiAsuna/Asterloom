# Asterloom

[简体中文](README.zh-CN.md) | [English](README.md)

Asterloom 是一个面向客户端应用与后端服务的统一基础能力平台，提供 Passport 登录认证、
权限控制、Feature Flag、定向与灰度发布、动态配置、桌面更新、Analytics、Telemetry、
gRPC/HTTP、文件存储和 PostgreSQL 持久化能力。

当前实现范围是 .NET/C# 后端与公共 C# SDK，以及 React/Next.js Web 管理后台；不包含
Rust、Go 或 C++ SDK。

## 核心能力

| 能力 | 用途 |
| --- | --- |
| Identity / Passport | 全局账号、应用成员关系、邀请、会话、OIDC/OAuth 2.0 登录、Client 与 Scope。 |
| Authorization | Permission、Role、Binding、Policy 和作用域权限判断。 |
| Targeting / Rollout | Segment、属性规则、模拟和稳定百分比分桶。 |
| Feature Flag | 类型化 Flag、Variant、草稿、发布、回滚与 OpenFeature 评估。 |
| Dynamic Config | 类型化配置、定向值、Diff、发布、快照和 Last-Known-Good。 |
| Desktop Update | Channel、签名 Artifact、Manifest、灰度发布及 Velopack 适配。 |
| Analytics | Event Schema、Write Key、批量摄取、脱敏、聚合和导出。 |
| Telemetry | OpenTelemetry Trace、Metric、Log、采样、Collector 健康和诊断跳转。 |
| RPC / HTTP | 一份 Protobuf 契约同时提供原生 gRPC 与 JSON Transcoding。 |
| File Storage | S3 兼容 Bucket/Object、签名上传下载、元数据和 SHA-256 验证。 |
| Persistence | PostgreSQL 模块独立 Schema、显式迁移和持久业务数据。 |
| Web Console | 完整操作全部管理 API，包含搜索、分页、错误状态、审计和浅色/深色主题。 |

## 架构概览

```text
Browser
  └─ HTTPS → Next.js Web Console / BFF
                 ├─ Redis：加密服务端 Session
                 └─ HTTP/JSON → Asterloom.Server

.NET Desktop / Backend Service
  └─ C# SDK / native gRPC → Asterloom.Server

Asterloom.Server
  ├─ Identity / Authorization / Targeting / Feature / Config
  ├─ Release / Analytics / Telemetry / Storage / RPC
  ├─ PostgreSQL
  ├─ S3-compatible object storage
  └─ OpenTelemetry Collector
```

系统首期采用 .NET 10 模块化单体。`Proto/Asterloom` 是 API 唯一契约源，所有自定义
业务 RPC 都开启 gRPC JSON Transcoding；原生 gRPC 与 HTTP/JSON 不重复实现业务逻辑。

浏览器只保存随机、不透明的 HttpOnly Session ID。OIDC Token 保存在 Next.js BFF
服务端，并在生产环境使用 Redis 共享和加密存储。具体决策见
[Redis BFF Session ADR](Docs/ADR/0001-redis-for-web-bff-sessions.md)。

## 技术栈

| 范围 | 技术 |
| --- | --- |
| Backend | .NET 10、ASP.NET Core、gRPC、JSON Transcoding、OpenIddict、Casbin.NET、Npgsql |
| C# SDK | Grpc.Net.Client、OpenFeature、HttpClient、System.Text.Json、OpenTelemetry、Velopack |
| Web | React 19、Next.js 16 App Router、TypeScript、Tailwind CSS 4、shadcn-ui、Radix UI |
| Web 数据层 | SWR、Zustand、Zod、Kiota、Redis Session、jose |
| 基础设施 | PostgreSQL 18、Redis 8、S3/MinIO、OpenTelemetry Collector、Nginx、Docker Compose |
| 测试 | xUnit、集成/契约测试、Vitest、Testing Library、Playwright |

## 仓库结构

```text
Backend/
  Asterloom.Server/             # 统一服务宿主
  Asterloom.Module.*/           # 领域模块与基础设施适配器
  Asterloom.Sdk.*/              # 公共 C# SDK
  Samples/                      # 全能力参考后台与客户端
  Tests/                        # Unit / Integration / Contract

Frontend/
  app/                          # Next.js App Router 与 BFF Route Handler
  components/                   # UI、布局与主题组件
  features/                     # 各领域管理工作区
  lib/                          # API、认证、状态和校验
  tests/                        # Unit / Playwright E2E

Proto/Asterloom/                # 版本化 Protobuf 唯一契约
Docs/                           # 架构、使用指南、ADR 与协议文档
Deploy/                         # Compose、Nginx、OpenTelemetry 与部署脚本
```

## 快速开始：Docker Compose

前置要求：Docker 与 Docker Compose。

```powershell
Copy-Item Deploy/.env.example .env
docker compose up --build
```

打开 `http://localhost:3000`。默认本地管理员为：

```text
Email:    admin@asterloom.local
Password: Asterloom-Local-Admin!2026
```

这些凭据和 `.env.example` 中的 Secret 只能用于本地开发，生产环境必须全部替换并通过
Secret Manager 注入。

默认本地端口：

| 服务 | 地址 |
| --- | --- |
| Web Console | `http://localhost:3000` |
| Server HTTP/JSON + Passport | `http://localhost:5080` |
| Server native gRPC | `http://localhost:5081` |
| PostgreSQL | `localhost:5432` |
| MinIO S3 / Console | `http://localhost:9000` / `http://localhost:9001` |
| OTLP gRPC / HTTP | `localhost:4317` / `localhost:4318` |

## 从源码运行

前置要求：`.NET SDK 10.0.400` 与 `Node.js 24+`。

先构建后端：

```powershell
dotnet restore Backend/Asterloom.sln
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln
```

不依赖 PostgreSQL/S3/Redis 的 Identity + BFF 本地开发模式可使用内存 Provider。第一个
终端运行：

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Persistence__Provider = "Memory"
$env:Identity__Issuer = "http://127.0.0.1:5080/"
$env:Identity__Bootstrap__AdminDisplayName = "Local Administrator"
$env:Identity__Bootstrap__AdminEmail = "admin@asterloom.local"
$env:Identity__Bootstrap__AdminPassword = "Asterloom-Local-Admin!2026"
$env:Identity__WebClient__ClientId = "asterloom-web"
$env:Identity__WebClient__ClientSecret = "Asterloom-Web-Local-Secret!2026"
$env:Identity__WebClient__RedirectUri = "http://localhost:3000/api/auth/callback"
$env:Identity__WebClient__PostLogoutRedirectUri = "http://localhost:3000/api/auth/logout/callback"
dotnet run --project Backend/Asterloom.Server --urls http://127.0.0.1:5080
```

第二个终端运行 Web：

```powershell
Set-Location Frontend
npm ci
$env:ASTERLOOM_BACKEND_URL = "http://127.0.0.1:5080"
$env:ASTERLOOM_PASSPORT_PUBLIC_URL = "http://127.0.0.1:5080"
$env:ASTERLOOM_WEB_ORIGIN = "http://localhost:3000"
$env:ASTERLOOM_OIDC_ISSUER = "http://127.0.0.1:5080"
$env:ASTERLOOM_OIDC_CLIENT_ID = "asterloom-web"
$env:ASTERLOOM_OIDC_CLIENT_SECRET = "Asterloom-Web-Local-Secret!2026"
$env:ASTERLOOM_SESSION_STORE = "memory"
npm run dev
```

`memory` Session Store 只能用于开发和测试；生产配置使用它时会拒绝启动。

## 如何使用各项能力

- [模块使用文档（中文）](Docs/Module/README.zh-CN.md)：按能力拆分的实施、Web、API、SDK、权限与运维文档。
- [Module Guides (English)](Docs/Module/README.md)：对应的英文模块索引。
- [业务应用统一账号接入](Docs/Module/Identity-Business-Integration.zh-CN.md)：全局账号、应用成员关系、业务后端注册登录与安全边界。
- [功能使用指南（中文）](Docs/Feature-Guide.zh-CN.md)：Web 操作顺序、全部能力说明和 C# SDK 示例。
- [Feature Usage Guide (English)](Docs/Feature-Guide.md)：对应的英文文档。
- [桌面自动更新指南](Docs/Module/Desktop-Updates.zh-CN.md)：RID、Velopack 打包、签名、上传、灰度与客户端安装。
- [文件存储指南](Docs/Module/File-Storage.zh-CN.md)：Bucket、对象传输、权限、Web 入口和 C# SDK 接入。
- [全能力参考应用](Docs/Reference-Application.md)：可执行的 Backend + Client、`provision`、`doctor` 与 `login`。

最简服务接入方式是先使用 Passport Client Credentials 获得 Token，再创建一个同时支持
gRPC 和 HTTP 的统一认证 Transport：

```csharp
var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
await identity.GetServiceAccessTokenAsync(cancellationToken: cancellationToken);

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);
```

之后 gRPC SDK 使用 `transport.CallInvoker`，Config、Release、Analytics 和 Storage 等
HTTP/传输 SDK 使用 `transport.HttpClient`。

## 全能力参考应用

仓库包含一个真实写入、读取和验证全部能力的 C# 后台与客户端：

```powershell
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- provision
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- doctor
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- login
$env:ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD = "Use-A-Strong-Test-Password!2026"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- account-demo user@example.com "Example User"
```

- `provision` 创建独立的完整测试资源。
- `doctor` 分别验证 13 类平台能力，某一项失败不会阻止其他诊断。
- `login` 使用系统浏览器验证 Passport Authorization Code + PKCE。
- `account-demo` 通过示例业务 BFF 验证统一账号注册、确认、密码登录、服务端 Session 与退出。

## 协议工作流

修改 `.proto` 后必须重新生成 OpenAPI 和 Web 内部 Kiota Client，并验证管理 API 与 UI
覆盖率：

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

覆盖检查要求每个自定义 RPC 都有 HTTP Mapping；每个 Admin RPC 都有 Permission、Web
Route、UI Action Marker 和 E2E Test。管理 API 不能以“后端完成、页面以后再补”的状态交付。

## Web 质量检查

```powershell
Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run test:e2e
```

生产环境回归使用 `npm run test:e2e:production`，管理员凭据通过
`ASTERLOOM_E2E_*` 环境变量注入，不能写入仓库。

## 数据库迁移与生产说明

- 生产 Schema 变更不会由 `Asterloom.Server` 隐式执行。
- 部署时先运行 `Backend/Tools/Asterloom.Migrations`，成功后再启动 Server。
- 生产必须使用 HTTPS、固定 OIDC Issuer、外部签名/加密证书和持久化 Data Protection Key。
- Redis、PostgreSQL、S3、Client Secret、Write Key、Session Encryption Key 等凭据必须从 Secret Manager 注入。
- Server 暴露 `/health/live`、`/health/ready` 和 `/health/startup`，部署后还应执行 Production Smoke Test 与参考应用 `doctor`。

## 文档

- [技术架构与实施基线](Docs/Architecture.md)
- [模块使用文档（中文）](Docs/Module/README.zh-CN.md)
- [Module Guides (English)](Docs/Module/README.md)
- [功能使用指南（中文）](Docs/Feature-Guide.zh-CN.md)
- [Feature Usage Guide (English)](Docs/Feature-Guide.md)
- [桌面自动更新指南](Docs/Module/Desktop-Updates.zh-CN.md)
- [Desktop Update Guide (English)](Docs/Module/Desktop-Updates.md)
- [文件存储指南](Docs/Module/File-Storage.zh-CN.md)
- [File Storage Guide (English)](Docs/Module/File-Storage.md)
- [全能力参考应用与诊断规范](Docs/Reference-Application.md)
- [标准协议端点](Docs/Protocol/standard-endpoints.md)
- [Redis Web BFF Session ADR](Docs/ADR/0001-redis-for-web-bff-sessions.md)
