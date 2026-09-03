# Asterloom 全能力参考应用与诊断规范

## 1. 目标

参考应用不是演示用的静态页面，而是 Asterloom 的长期端到端契约：使用公开 C# SDK、原生 gRPC 和 gRPC JSON Transcoding 对平台能力做真实写入、读取和校验。任一能力失败时必须单独报告模块、状态码、gRPC 状态和错误正文，禁止退化成统一的 `An unexpected error occurred.`。

它由三个 C# 项目组成：

| 项目 | 作用 |
| --- | --- |
| `Asterloom.ReferenceApp.Contracts` | 示例应用自己的 protobuf 契约，不污染 Asterloom 核心协议和 OpenAPI。 |
| `Asterloom.ReferenceApp.Backend` | ASP.NET Core 后台；同时提供原生 gRPC 与 JSON Transcoding，并使用 Npgsql 写入 PostgreSQL。 |
| `Asterloom.ReferenceApp.Client` | 控制台/桌面宿主客户端；负责 Passport 登录、平台数据准备及全能力诊断。 |

调用关系：

```text
Reference Client
  ├─ Passport (Public Client authorization code + PKCE)
  │    └─ user Access Token → Reference Backend protected API
  │          └─ business DB attributes + Confidential Client → RBAC/ACL/ABAC decision
  ├─ Service identity (Confidential Client / client credentials)
  ├─ Business account registration → Reference Backend
  ├─ Asterloom gRPC + JSON/HTTP
  │    ├─ Authorization
  │    ├─ Targeting / Feature / Rollout
  │    ├─ Config
  │    ├─ Release / signed artifact download
  │    ├─ Analytics
  │    ├─ Telemetry management
  │    ├─ Mail delivery
  │    ├─ Storage
  │    └─ Operations / OpenAPI
  ├─ OTLP → OpenTelemetry Collector
  └─ Reference Backend
       ├─ native gRPC :5091
       ├─ JSON Transcoding :5090
       └─ Npgsql → PostgreSQL reference_app schema
```

## 2. 命令

客户端包含五个对外命令：

```bash
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- provision
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- doctor
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- login
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- login --authorization-demo
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- account-demo user@example.com "Example User"
# 下面的 update 必须从 Velopack 安装目录中的程序运行，不能用 dotnet run。
Asterloom.ReferenceApp.Client.exe update update-result.json [--force-full]
```

- `provision`：创建独立的 tenant/application/environment，以及分群、已发布 Feature Flag、已发布动态配置、存储桶、签名密钥、更新通道、签名更新包、Analytics Schema/Write Key 和 Telemetry Source；敏感状态只写入被 Git 忽略的 `reference-state.json`。
- `doctor`：逐项运行诊断。某项失败不会阻止后续能力，进程最终以非零退出码表示存在失败；加 `--json` 可供 CI 和监控采集。
- `login`：启动系统浏览器，以 Public Client + OIDC Authorization Code + PKCE 登录 Passport，再把用户 Access Token 作为 Bearer 调用参考后台 `/api/reference/me`。参考后台会验证公开签名、Issuer、Audience、用户类型和 Tenant/Application 绑定；此命令不需要 Client Secret，也不在无界面的容器中执行。加 `--authorization-demo` 后还会写入一份明确标记为测试用途的业务订单/部门 Fixture，再调用退款 API；退款 API 从自己的 PostgreSQL 读取权威属性，并用业务 Confidential Client 代表当前 `sub` 请求 Asterloom RBAC/ACL/ABAC 决策。
- `account-demo`：通过参考后台真实执行业务用户注册和邮箱确认；随后使用 `login` 通过标准 PKCE 登录并验证应用绑定的 `tenant_id`、`application_id` 和全局 `sub`。
- `update`：仅在真实 Velopack 安装态运行；经 Asterloom 检查、下载并应用更新，重启到新版本后把下载的
  `Delta`/`Full` 类型、前后版本和 Restart Hook 结果写入指定 JSON。`--force-full` 禁用 Delta，用于验证 Full 回退。

`account-demo` 从 `ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD` 读取初始密码，避免密码进入命令历史。
`account-demo` 在 Reference Backend 启用 `Asterloom:Mail` 时会通过业务 Confidential Client 和
`Asterloom.Sdk.Mail` 发送邮箱确认内容；没有接入真实邮件发送时，需要在执行 Provision 脚本前显式设置
`ASTERLOOM_REFERENCE_EXPOSE_CONFIRMATION_TOKEN=true` 并重建参考后台；该开关默认关闭，禁止在公开生产入口启用。

服务身份和原生客户端由管理面完成一次性注册：

```bash
bash Deploy/Scripts/Provision-Reference-App.sh
```

该脚本使用 Web BFF 登录，不把管理员 access token 暴露给浏览器脚本；它创建/轮换
`asterloom-reference-service`，创建 `asterloom.reference.api` → `asterloom-reference-api` 资源 Audience，创建并
绑定 `asterloom-reference-native` Public Client，同时创建独立的
`asterloom-reference-business` Confidential Client。业务 Client 绑定到专用 Platform Application，启用
Client Credentials 和可信注册；用户登录只由独立的 Public Client 通过 Authorization Code + S256 PKCE 完成。生成的密钥只保存在
`.data/reference-app/reference.env`，权限为 `0600`；可写状态单独位于 `.data/reference-app/state`，生产容器仍以
非 root UID 1654 运行。

脚本还会幂等创建 Application Permission `orders.refund`，以及一条仅匹配 `order` 资源、
`subject.department=finance` 且 `resource.amount<=5000` 的 ABAC Allow Policy。`--authorization-demo` 使用的
Fixture 接口只用于演示如何准备业务自己的权威数据，生产业务不能开放“用户自行填写授权属性”的接口。

同一 `reference.env` 还会为 Reference Backend 写入 Resource Server 的 Issuer、Audience、TenantId 和
ApplicationId。修改 Scope Resource、Application 绑定或域名后必须重新执行脚本并重建参考后台。

## 3. 环境变量

| 名称 | 必需 | 说明 |
| --- | --- | --- |
| `ASTERLOOM_REFERENCE_CLIENT_ID` | service 命令需要 | confidential service client ID；`login` 不读取。 |
| `ASTERLOOM_REFERENCE_CLIENT_SECRET` | service 命令需要 | service client secret；`login` 不读取。 |
| `ASTERLOOM_BASE_URL` | 否 | Asterloom gRPC/HTTP 地址；默认生产域名。 |
| `ASTERLOOM_ISSUER` | 否 | Passport issuer；默认同平台地址。 |
| `ASTERLOOM_REFERENCE_BACKEND_URL` | 否 | 参考后台 JSON Transcoding 地址。 |
| `ASTERLOOM_REFERENCE_BACKEND_GRPC_URL` | 否 | 参考后台原生 gRPC 地址。 |
| `ASTERLOOM_REFERENCE_INTERACTIVE_CLIENT_ID` | 否 | public native OIDC client ID。 |
| `ASTERLOOM_REFERENCE_API_SCOPE` | 否 | Public Client 请求的业务 API Scope；默认 `asterloom.reference.api`。 |
| `ASTERLOOM_REFERENCE_API_AUDIENCE` | Provision 脚本可选 | Reference Backend 验证的 Audience；默认 `asterloom-reference-api`。 |
| `ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD` | `account-demo` 需要 | 新业务账号的初始密码，只从环境读取。 |
| `Asterloom__Mail__Enabled` | 否 | 为 Reference Backend 启用真实确认邮件发送。 |
| `Asterloom__Mail__SmtpAccountId` | Mail 启用时 | Web 控制台中创建的 SMTP Account UUID。 |
| `Asterloom__Mail__TenantId` / `Asterloom__Mail__ApplicationId` | Mail 启用时 | SMTP 账号所属作用域；必须与业务 Confidential Client 的绑定一致。 |
| `ASTERLOOM_REFERENCE_STATE_FILE` | 否 | provision 产生的状态文件。 |
| `ASTERLOOM_REFERENCE_RELEASE_PACKAGE_ID` | 真实更新测试 | 必须与 `vpk --packId` 相同。 |
| `ASTERLOOM_REFERENCE_RELEASE_RUNTIME_ID` | 真实更新测试 | Artifact RID，例如 `win-x64`。 |
| `ASTERLOOM_REFERENCE_RELEASE_BASE_VERSION` | 真实更新测试 | 已安装基线版本。 |
| `ASTERLOOM_REFERENCE_RELEASE_TARGET_VERSION` | 真实更新测试 | 目标版本。 |
| `ASTERLOOM_REFERENCE_RELEASE_BASE_FULL` | 真实更新测试 | 基线 Full `.nupkg` 路径。 |
| `ASTERLOOM_REFERENCE_RELEASE_TARGET_FULL` | 真实更新测试 | 目标 Full `.nupkg` 路径。 |
| `ASTERLOOM_REFERENCE_RELEASE_TARGET_DELTA` | 真实更新测试 | 从基线到目标的 Delta `.nupkg` 路径。 |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | 否 | OTLP Collector 地址；未设置时禁用导出。 |
| `ASTERLOOM_REFERENCE_OTEL_METRICS_URL` | `doctor` 的 Telemetry 项 | Collector 内部 Prometheus 指标地址；用于断言三类信号已被 Receiver 接收并由 Exporter 输出。 |

除显式启用的 loopback 开发环境外，Passport 和平台地址必须使用 HTTPS。

## 4. 能力覆盖和成功条件

| 能力 | 真实操作 | 成功条件 |
| --- | --- | --- |
| Identity | OIDC discovery、client credentials、受保护 Identity API；另有 Public Client PKCE、业务 Bearer API 和业务账号命令 | 能取得服务 Token；discovery 不公布 Password Grant 且只公布 S256 PKCE；公开 `at+jwt` 可由 JWKS 验签；Reference Backend 接受用户 Access Token 且拒绝 ID Token；业务注册/确认全链路成功。 |
| Authorization | 管理面 Application Permission；`CheckPermissionAsync`；Public User Token + 业务 Confidential Client `CheckAccessAsync` | RBAC 基础检查成功；ACL 资源匹配；ABAC 使用业务 PostgreSQL 属性允许 finance/低金额退款；跨应用、非成员或不匹配条件拒绝。 |
| Targeting | `AsterloomTargetingAdminClient.ListSegmentsAsync` | PostgreSQL 中创建的 segment 可由 gRPC SDK 读取。 |
| Feature Flag | `AsterloomFeatureProvider` | CN context 命中 segment 并返回 `on/true`。 |
| Rollout | Feature allocation 与 Release 100% rollout | 产生稳定且可解释的命中结果。 |
| Dynamic Config | `AsterloomConfigClient` snapshot/typed getter | CN context 返回目标值，snapshot 包含配置 key。 |
| Desktop Update | 真实 `vpk` Full/Delta、`AsterloomVelopackUpdateSource`、安装态 `UpdateManager` | Delta 下载并逐字节还原目标 Full；真实安装程序从基线更新、替换、重启到目标版本；可强制验证 Full 回退。 |
| Analytics | `AsterloomAnalyticsClient.TrackAsync/FlushAsync` | Schema 校验通过且 accepted=1、remaining=0。 |
| Telemetry | 自定义 Activity/Meter/Log + OTLP；读取 source/collector health 和 Collector 内部计数器 | Collector 必须为 Healthy，且三类信号的 Receiver accepted 与 Exporter sent 计数均增加。 |
| Mail | 注册后由 Reference Backend 提交确认邮件；受保护测试端点提交业务邮件 | 使用绑定应用的 SMTP 账号返回 `SENT`，投递历史可从控制台查看。 |
| RPC | 调用参考后台 `RecordHeartbeat` | 原生 HTTP/2 gRPC 成功返回 heartbeat ID。 |
| HTTP | 调用同一 protobuf 的 JSON 路由 | HTTP/1.1 POST/GET 成功，证明浏览器可用 Transcoding。 |
| File Storage | SDK upload/complete/download | 对象可读回且字节完全一致，SHA-256 一致。 |
| Persistence | 参考后台 Npgsql 查询 | 新 heartbeat 可从 PostgreSQL `reference_app.client_heartbeats` 读回。 |
| Operations | health、API catalog、OpenAPI，以及各后台主列表 | 全部返回 2xx；用于捕获缺表、权限映射和代理错误。 |

未配置三个 Package 路径时，`provision` 仍创建轻量签名探针供常规 13 项 `doctor` 使用；三个路径全部配置时，
它会依次发布基线 Full，以及包含目标 Full + Delta 的目标版本。路径只允许全部提供或全部省略，避免把半套 Release
误判为差分更新成功。

### 4.1 真实桌面更新回归

仓库固定使用 Velopack 1.2.0。脚本会构建 Sample 1.0.0/1.1.0，生成真实 Full/Delta 和 1.0.0 Setup，随后用
`vpk delta patch` 还原目标 Full 并比较 SHA-256；哈希不同会立即失败：

```powershell
./Deploy/Scripts/Build-Reference-DesktopUpdate.ps1 `
  -OutputDirectory "$env:TEMP/asterloom-reference-update"
```

把脚本输出的三个 `.nupkg` 路径配置到上述环境变量后运行 `provision`。安装基线 Setup，再从安装目录运行：

```powershell
Asterloom.ReferenceApp.Client.exe update "$env:TEMP/delta-result.json"
# 卸载并重新安装 1.0.0 后，单独验证 Full 路径：
Asterloom.ReferenceApp.Client.exe update "$env:TEMP/full-result.json" --force-full
```

成功凭证必须同时满足：Delta 场景只从 Asterloom 下载 `Delta`，Full 场景只下载 `Full`，两次均触发
`OnRestarted`，且重启后的程序集版本等于目标版本。直接从 `bin/` 或 Portable 包运行会被拒绝，因为它不能证明真实替换。

## 5. 生产部署与运行

生产 Compose 新增 `reference-backend`，Nginx 路由如下：

- `/api/reference/*` → 参考后台 JSON Transcoding (`127.0.0.1:60004`)
- `/asterloom.reference.v1.ReferenceAppService/*` → 参考后台原生 gRPC (`127.0.0.1:60005`)
- 其他 `/asterloom.*` → Asterloom Server 专用 HTTP/2 端口 (`127.0.0.1:60002`)，HTTP/JSON 使用 `127.0.0.1:60001`。

生产执行：

```bash
bash Deploy/Scripts/Provision-Reference-App.sh
docker compose -f docker-compose.yml -f Deploy/docker-compose.production.yml up -d --force-recreate otel-collector reference-backend
docker compose -f docker-compose.yml -f Deploy/docker-compose.production.yml \
  --profile reference run --rm --no-deps reference-client provision --json
docker compose -f docker-compose.yml -f Deploy/docker-compose.production.yml \
  --profile reference run --rm --no-deps reference-client doctor --json
```

`doctor` 的 JSON 输出应由 CI 或定时监控保存；任何 `succeeded=false` 都应视为平台回归。

## 6. 本轮生产问题与修复

Web 的多个 `An unexpected error occurred.` 并非前端统一主题问题，而是生产迁移工具只注册了 Platform、Authorization、Audit 和 Infrastructure。Server 已启用但迁移工具漏掉 Targeting、Feature、Config、Storage、Release、Analytics、Telemetry，导致页面查询不存在的表，例如 `release.channels`、`targeting.segments`、`telemetry.recent_errors`。

迁移工具现已与 Server 的模块注册对齐，并由数据库集成测试固定迁移总数。参考客户端的 Operations 步骤还会持续访问这些模块主列表，防止以后再次出现“模块已启动、表未迁移”的静默漂移。

参考应用继续发现并固定了以下部署/SDK 边界：

| 问题 | 根因 | 修复 |
| --- | --- | --- |
| 参考客户端拿不到 service secret | Compose 的 `environment` 空值覆盖了 `env_file` | 不再声明空 secret；凭据只由权限为 `0600` 的 `reference.env` 注入。 |
| Release/Storage 预签名传输返回 S3 `multiple authentication types` | 统一 HttpClient 又给 AWS Signature V4 URL 添加 Bearer | API 与对象传输使用不同 HttpClient；公共 Bearer Handler 同时跳过预签名 URL，并禁止向非 Asterloom origin 泄漏 token。 |
| `reference-state.json` 无法写入 | bind mount 为 `root:root`，容器以 UID 1654 运行 | 凭据目录与可写状态目录分离，state 目录只授权给 UID 1654，容器保持非 root。 |
| Authorization/Targeting/Feature 原生 gRPC 返回 502 | 明文单端口无法在 HTTP/1.1 和 HTTP/2 间通过 ALPN 协商 | Asterloom Server 使用 `8000/Http1` 与 `8001/Http2` 独立容器端点；Nginx gRPC upstream 使用宿主机端口 `60002`。 |
| `compose run` 重建平台依赖 | 一次性诊断命令默认启动 `depends_on` | 生产命令统一使用 `run --rm --no-deps`，参考后端和平台服务单独常驻。 |
| PostgreSQL 首次连接打印缺少 `libgssapi` | Npgsql 默认优先探测 GSS，而容器部署只使用密码认证 | 容器连接串显式设置 `GSS Encryption Mode=Disable`，避免无意义的 Kerberos native library 探测。 |

## 7. 2026-08-31 验收基线

- 生产 `provision` 成功创建全套资源，`doctor` 13/13 通过。
- .NET Unit 49、Integration 9、Contract 21，共 79 项通过。
- Web Vitest 34 项、本地 Chromium Playwright E2E 17 项全部通过；E2E 覆盖全部管理能力、Passport/BFF、统一账号应用成员关系和浅色主题。
- ESLint、TypeScript、Next.js production build 与生产 Smoke Test 全部通过。

## 8. Web 生产回归

生产回归使用独立配置，不启动本地 `webServer`，也不在仓库中保存管理员凭据：

```powershell
$env:ASTERLOOM_E2E_WEB_ORIGIN = "https://asterloom.momiya.cloud"
$env:ASTERLOOM_E2E_PASSPORT_ORIGIN = "https://asterloom.momiya.cloud"
$env:ASTERLOOM_E2E_API_ORIGIN = "https://asterloom.momiya.cloud"
$env:ASTERLOOM_E2E_ADMIN_EMAIL = "<production-admin-email>"
$env:ASTERLOOM_E2E_ADMIN_PASSWORD = "<production-admin-password>"
npm --prefix Frontend run test:e2e:production
```

生产配置固定单 worker，并在失败时保留 screenshot、video 和 trace。测试数据使用每次运行唯一的 slug、actor 和策略名称，避免历史数据造成定位歧义。

本轮 Web 回归发现并修复：

| 问题 | 根因 | 修复 |
| --- | --- | --- |
| Config 连续保存、校验、发布偶发版本冲突 | 异步保存尚未完成时后续按钮仍可点击 | mutation 期间禁用相关操作，并等待最新 draft version。 |
| Storage/Release 预签名上传返回 `AccessDenied` | Kiota metadata/header 已标准化后被二次解析，签名所需的 `x-amz-meta-*` header 被丢弃 | transfer ticket schema 同时接受 Kiota `additionalData` 和已标准化字典，并增加回归单测。 |
| 租户超过 25 条后，新建 Targeting/Telemetry scope 无法继续 | 新租户落在分页之外，页面仍在第一页，选中 ID 无法解析为当前页记录 | 创建租户、应用、环境后自动按新 slug 定位；分页数量不再影响后续操作。 |
| Authorization 重跑后定位器命中多条历史策略 | 策略描述使用固定测试值 | 策略名称加入本轮唯一后缀，保持生产回归可重复执行。 |

2026-08-31 最终连续生产回归结果为 `15 passed (5.1m)`；随后生产 Smoke Test 通过，参考客户端 `doctor --json` 为 13/13，Asterloom 相关容器近 20 分钟无 severe 日志，Nginx 最近 5000 条请求无 5xx。
