# Asterloom 开发与部署指南

[简体中文](README.zh-CN.md) | [English](README.md)

本文档说明如何在本地开发 Asterloom、如何部署当前生产拓扑，以及 `Deploy/`
目录中每个文件的职责。模块 API 和 SDK 的使用方法请参阅
[模块文档](../Docs/Module/README.zh-CN.md)。

## 1. 先选择运行方式

| 场景 | 推荐方式 | 使用的主要文件 |
| --- | --- | --- |
| 快速启动完整本地环境 | Docker Compose | `../docker-compose.yml`、`.env.example` |
| 修改后端或 Web 并快速调试 | 从源码运行 | `global.json`、`Frontend/package.json` |
| 部署当前生产环境 | Compose + 宿主机 Nginx | `../docker-compose.yml`、`docker-compose.production.yml`、`Nginx/*`、宿主机脚本 |
| 构建可搬运的预编译制品 | 预构建脚本 | `Build-Server-Prebuilt.sh`、`Build-Web-Prebuilt.sh`、两个 `Dockerfile.*-prebuilt` |
| 验证参考应用或桌面更新 | 参考应用脚本 | `Provision-Reference-App.sh`、`Build-Reference-DesktopUpdate.ps1` |

生产 Compose 文件是根目录 Compose 的覆盖文件，不能单独运行。所有生产 Compose
命令都必须同时传入两个文件，并保持顺序不变：

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  <command>
```

## 2. 本地开发

### 2.1 前置要求

- 完整容器方式：Docker Engine 或 Docker Desktop，以及 Docker Compose v2。
- 源码方式：[`global.json`](../global.json) 指定的 .NET SDK 10.0.400，以及
  Node.js 24 或更高版本。
- 修改协议时还需要 PowerShell 7 和仓库中声明的 .NET Local Tools。

### 2.2 使用 Docker Compose 启动完整环境

从仓库根目录执行：

```powershell
Copy-Item Deploy/.env.example .env
docker compose up --build -d
docker compose ps -a
docker compose logs -f migrations server web
```

Linux/macOS 上把第一行改为：

```bash
cp Deploy/.env.example .env
```

`migrations` 是一次性容器。它成功退出并显示 `Exited (0)` 属于正常状态；只有迁移成功后
`server` 才会启动。打开 `http://localhost:60000`，使用以下仅供本地开发的管理员登录：

```text
Email:    admin@asterloom.local
Password: Asterloom-Local-Admin!2026
```

本地 Compose 的宿主机端口统一保留在 `60000–60010`，当前使用 `60000–60005`，
`60006–60010` 预留。容器之间应使用 Compose Service 名和容器端口，例如 Web BFF
访问 `http://server:8000`，不能使用宿主机映射端口：

| Compose Service / 监听器 | 容器内端口 | 本地宿主机端口 | 说明 |
| --- | --- | --- | --- |
| `web` | `3000` | `60000` | 管理页面以及 `/api/auth/*`、`/api/asterloom/*` BFF 路由。 |
| `server` HTTP/JSON / Passport | `8000` | `60001` | HTTP/1.1、JSON Transcoding、OIDC/OAuth 和健康检查。 |
| `server` 原生 gRPC | `8001` | `60002` | HTTP/2 原生 gRPC；浏览器不直接连接。 |
| `reference-backend` HTTP/JSON | `5090` | `60004` | 参考应用的 JSON Transcoding 入口。 |
| `reference-backend` 原生 gRPC | `5091` | `60005` | 参考应用的 HTTP/2 gRPC 入口。 |
| `postgres` | `5432` | 不映射 | 仅 Compose 网络可达。 |
| `redis` | `6379` | 不映射 | 只在 Compose 网络中存储 Web BFF Session。 |
| `minio` S3 API | `9000` | `60003` | S3 兼容对象传输。 |
| `minio` Console | `9001` | 不映射 | 仅 Compose 网络可达。 |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | 不映射 | 仅 Compose 网络接收 OpenTelemetry 数据。 |
| `otel-collector` Health | `13133` | 不映射 | 仅 Compose 网络可达。 |
| `migrations` / `reference-client` | 无 | 无 | 执行命令后退出的一次性容器，不启动网络监听器。 |

常用生命周期命令：

```powershell
docker compose logs -f server web
docker compose up -d --build
docker compose down
```

`docker compose down` 保留命名卷。`docker compose down -v` 会删除 PostgreSQL、MinIO、
Redis 和 Data Protection 等本地数据，只能在确认需要重置环境时使用。

### 2.3 从源码运行后端和 Web

后端的 Development 配置可使用内存持久化，适合 Identity、BFF 和管理 UI 的快速调试，
但不会验证 PostgreSQL、MinIO、Redis 或 Collector 的真实集成。
本节的 `5080/3000` 是直接启动进程时的开发端口，不是上表的容器映射。

第一个 PowerShell 终端：

```powershell
dotnet restore Backend/Asterloom.sln
dotnet build Backend/Asterloom.sln

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

第二个 PowerShell 终端：

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

生产环境不允许使用内存 Session Store。完整集成调试应使用 Compose，它会让 Web BFF
通过 Redis 保存加密的服务端 Session，并让 Server 使用 PostgreSQL 和 S3。

### 2.4 开发质量检查

```powershell
dotnet test Backend/Asterloom.sln --configuration Release

Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npx playwright install chromium
npm run test:e2e
Set-Location ..
```

修改 `.proto` 后还必须执行：

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1 -Configuration Release
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

生成的 OpenAPI 和 Kiota Client 需要随代码一起提交。

## 3. 当前生产拓扑

当前生产资产以 `asterloom.kirayuukiasuna.cloud` 为目标域名，采用宿主机 Nginx 和容器化
应用：

```text
Internet :80/:443
  └─ host Nginx
       ├─ Web / BFF                    → 127.0.0.1:60000
       ├─ Asterloom HTTP/JSON/OIDC     → 127.0.0.1:60001
       ├─ Asterloom native gRPC        → 127.0.0.1:60002
       ├─ signed object transfer        → 127.0.0.1:60003
       ├─ Reference HTTP/JSON           → 127.0.0.1:60004
       └─ Reference native gRPC         → 127.0.0.1:60005

Docker network only
  ├─ PostgreSQL
  ├─ Redis
  └─ OpenTelemetry Collector
```

Nginx 不在容器中运行。生产覆盖文件把应用端口只绑定到 `127.0.0.1`；PostgreSQL、Redis、
Collector 和 MinIO Console 不对宿主机或公网直接开放。公网通常只需开放 `80/tcp`、
`443/tcp` 和运维所需的 SSH 端口。

生产环境的容器端口及宿主机绑定如下：

| Compose Service / 监听器 | 容器内端口 | 生产宿主机绑定 | 公网入口 |
| --- | --- | --- | --- |
| `web` | `3000` | `127.0.0.1:60000` | Nginx 的默认 Web/BFF 路由。 |
| `server` HTTP/JSON / Passport | `8000` | `127.0.0.1:60001` | Nginx HTTP upstream。 |
| `server` 原生 gRPC | `8001` | `127.0.0.1:60002` | Nginx `grpc_pass` upstream。 |
| `minio` S3 API | `9000` | `127.0.0.1:60003` | `/asterloom-objects/*` 预签名传输。 |
| `reference-backend` HTTP/JSON | `5090` | `127.0.0.1:60004` | `/api/reference/*`。 |
| `reference-backend` 原生 gRPC | `5091` | `127.0.0.1:60005` | ReferenceAppService 原生 gRPC 路由。 |
| `minio` Console | `9001` | 不映射 | 仅 Compose 网络可达，不提供公网管理界面。 |
| `postgres` | `5432` | 不映射 | 仅 Compose 网络可达。 |
| `redis` | `6379` | 不映射 | 仅 Compose 网络可达。 |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | 不映射 | 仅 Compose 网络可达。 |
| `otel-collector` Health | `13133` | 不映射 | 仅 Compose 网络可达。 |
| `migrations` / `reference-client` | 无 | 无 | 一次性容器，无监听端口。 |

“不映射”只表示没有发布到宿主机；同一 Compose 网络内的容器仍可通过
`<service-name>:<container-port>` 访问。例如 Server 使用 `postgres:5432`、
`minio:9000` 和 `otel-collector:4317`。

主要路由：

| 公网路径 | 上游 | 用途 |
| --- | --- | --- |
| `/.well-known/*`、`/connect/*`、`/passport/*` | Server HTTP | OIDC discovery、OAuth 端点和 Passport 页面。 |
| `/api/v1/*`、`/health/*` | Server HTTP | JSON Transcoding API 和健康检查。 |
| `/asterloom.<service>/<method>` | Server gRPC | 原生 gRPC。 |
| `/api/reference/*` | Reference HTTP | 参考后台 JSON Transcoding。 |
| `/asterloom.reference.v1.ReferenceAppService/*` | Reference gRPC | 参考后台原生 gRPC。 |
| `/asterloom-objects/*` | MinIO | S3 预签名上传和下载。 |
| 其他路径 | Web | Next.js Console 和 BFF。 |

## 4. 首次生产部署

### 4.1 宿主机要求

当前脚本面向使用 systemd 的 Linux 主机，并假定以下工具已经安装：

- Docker Engine 和 Compose v2 插件；
- Nginx、Certbot、OpenSSL；
- Git、Bash、curl、jq、sed 和 awk；
- 已指向该主机的域名，且外部可以访问 80/443 端口。

脚本不会安装这些系统软件，也不会配置云防火墙或 DNS。执行 Compose 的运维账号还必须
有权访问 Docker Daemon；否则应按宿主机策略为 Docker 命令使用 `sudo`。

### 4.2 域名定制边界

当前域名写在以下生产资产中：

- `docker-compose.production.yml`：Issuer、OIDC 回调地址和 Storage 公网地址；
- `Nginx/asterloom.bootstrap.conf` 与 `Nginx/asterloom.conf`：`server_name` 和 TLS 路径；
- `Scripts/Prepare-ProductionHost.sh`：Nginx Site 文件名和默认管理员邮箱；
- `Scripts/Enable-ProductionTls.sh`：Certbot 域名、证书名和默认联系邮箱。

部署到其他域名时必须一致修改以上文件。`Provision-Reference-App.sh` 和
`Smoke-Test-Production.sh` 支持临时设置 `ASTERLOOM_DOMAIN`，但该变量不会自动改写
Compose 或 Nginx 配置。

### 4.3 拉取代码并准备 Secret

```bash
sudo mkdir -p /home/Dev
sudo chown "$USER:$USER" /home/Dev
cd /home/Dev
git clone <repository-url> Asterloom
cd Asterloom
sudo bash Deploy/Scripts/Prepare-ProductionHost.sh
```

`Prepare-ProductionHost.sh` 会：

1. 仅在根目录 `.env` 不存在时生成生产 Secret；
2. 创建两个供 OpenIddict 使用的 Token 签名/加密 PFX；
3. 创建持久化 Data Protection Key 目录并设置容器 UID 1654 的权限；
4. 安装仅支持 ACME Challenge 的临时 Nginx Site，然后校验并 reload Nginx。

它不会覆盖已有 `.env` 或 PFX。若 `.env` 已存在，至少应安全设置以下变量：

| 变量 | 用途 |
| --- | --- |
| `POSTGRES_PASSWORD` | PostgreSQL 应用账号。 |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | S3 兼容对象存储。 |
| `REDIS_PASSWORD` | BFF Session Redis。 |
| `ASTERLOOM_BOOTSTRAP_ADMIN_NAME` | 首个管理账号显示名。 |
| `ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL` / `ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD` | 首个管理账号凭据。 |
| `ASTERLOOM_OIDC_CLIENT_SECRET` | Web BFF 的 OIDC Confidential Client Secret。 |
| `ASTERLOOM_SESSION_ENCRYPTION_KEY` | 32 字节 Base64 Key，用于加密 Redis 中的 Session。 |
| `ASTERLOOM_CERTIFICATE_PASSWORD` | 两个 OpenIddict PFX 的密码；生产覆盖文件强制要求。 |

不要直接把本地 `.env.example` 用于生产。根目录 `.env`、`Deploy/Secrets/` 和 `.data/`
都已被 Git 忽略，仍应设置严格权限并纳入安全的 Secret/备份方案。准备完成后请从 `.env`
安全保存生成的管理员密码；应用不会再次显示它。

Token 签名/加密 PFX 与 HTTPS 证书不是同一类证书：前者由 Server 在容器内签发和保护
Token，后者由 Certbot 生成并由宿主机 Nginx 终止 TLS。

### 4.4 获取 TLS 证书

确认 DNS 已生效且 Nginx 的 `/.well-known/acme-challenge/` 可从公网访问，然后执行：

```bash
sudo CERTBOT_EMAIL=admin@example.com \
  bash Deploy/Scripts/Enable-ProductionTls.sh
```

脚本使用 Certbot Webroot 模式申请/复用证书，随后把临时 Nginx Site 替换为完整 HTTPS
代理配置。还应根据宿主机发行版验证自动续期和 Nginx reload：

```bash
sudo certbot renew --dry-run
sudo nginx -t
```

### 4.5 构建并启动应用

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --build --remove-orphans
```

Compose 会先启动 PostgreSQL，再执行一次性 `migrations`，迁移成功后才启动 Server。
生产 Schema 不由 `Asterloom.Server` 在启动时隐式修改。

检查状态和日志：

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  ps -a

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  logs --tail=200 migrations server web
```

`nginx` 是宿主机服务，不是 Compose Service；应使用以下命令查看它：

```bash
sudo systemctl status nginx
sudo tail -n 200 /var/log/nginx/asterloom.error.log
```

### 4.6 部署验证

```bash
curl --fail https://asterloom.kirayuukiasuna.cloud/health/live
curl --fail https://asterloom.kirayuukiasuna.cloud/health/ready
sudo bash Deploy/Scripts/Smoke-Test-Production.sh
```

Smoke Test 会使用 `.env` 中的管理账号执行完整的 HTTPS、Passport、OIDC、Web BFF
Session、受保护 JSON API 和退出流程。它需要 `curl`、`jq`、`sed` 和 `awk`。

参考应用是可选的全能力诊断。在核心平台已经可用后执行：

```bash
sudo bash Deploy/Scripts/Provision-Reference-App.sh

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --force-recreate reference-backend

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  --profile reference run --rm --no-deps reference-client provision --json

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  --profile reference run --rm --no-deps reference-client doctor --json
```

Provision 脚本会创建或更新参考应用的 OIDC Client，并轮换两个 Client Secret；因此执行后
必须重建 `reference-backend`。详细行为见
[全能力参考应用文档](../Docs/Reference-Application.md)。

## 5. 后续发布和运维

### 5.1 更新版本

```bash
cd /home/Dev/Asterloom
git pull --ff-only

sudo install -m 0644 \
  Deploy/Nginx/asterloom.conf \
  /etc/nginx/sites-available/asterloom.kirayuukiasuna.cloud
sudo nginx -t

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  build server web reference-backend
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --no-build --remove-orphans
sudo systemctl reload nginx
sudo bash Deploy/Scripts/Smoke-Test-Production.sh
```

先构建镜像可让旧容器在构建期间继续提供服务；`up` 会使用新镜像重建服务，并再次通过
一次性迁移服务确保数据库 Schema 已更新。宿主机 Nginx 配置不会由 Compose 自动更新，
因此端口或路由变更必须安装、校验并 reload `Deploy/Nginx/asterloom.conf`。不要只用
`docker compose restart` 应用镜像或环境变量变更，因为 restart 不会重建容器。

### 5.2 常用排障命令

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  ps -a

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  logs -f --tail=200 server web postgres redis minio otel-collector

sudo nginx -t
sudo journalctl -u nginx --since "30 minutes ago"
```

排查顺序建议为：`migrations` 是否成功、Server `/health/ready` 是否正常、Web/BFF 是否能
创建 Redis Session、Nginx 上游端口是否监听，最后再检查浏览器行为。

### 5.3 持久数据与备份

至少应备份或具备重建方案的内容包括：

- PostgreSQL 数据库；
- MinIO 对象数据；
- 根目录 `.env`；
- `Deploy/Secrets/*.pfx`；
- `.data/dataprotection-keys/`；
- 使用参考应用时的 `.data/reference-app/reference.env` 与 `state/`；
- 宿主机 `/etc/letsencrypt/` 和 Nginx Site 配置。

Redis 主要保存可失效的 BFF Session，但若需要无感会话连续性，也应把它纳入恢复设计。
不要把 `docker compose down -v` 当作普通停止命令；它会删除生产命名卷。数据库迁移是前向
执行的，代码回滚前必须单独评估 Schema 兼容性，不能假设回退 Git 提交会自动回退数据库。

当前 Collector 只使用 `debug` exporter，把 Trace、Metric 和 Log 输出到 Collector 日志，
并不是持久化可观测性后端。正式接入 OTLP、Prometheus、Loki 或其他后端时，应修改
`OpenTelemetry/otel-collector.yaml` 并配置对应凭据和网络。

## 6. `Deploy/` 文件说明

### 6.1 顶层文件

| 文件 | 何时使用 | 作用和注意事项 |
| --- | --- | --- |
| [`README.md`](README.md) | 查阅文档 | 本指南的英文版。 |
| [`README.zh-CN.md`](README.zh-CN.md) | 查阅文档 | 本指南的中文版。 |
| [`.env.example`](.env.example) | 本地 Compose | 本地默认数据库、MinIO、Redis、管理员、OIDC 和 Session Key 示例。复制到仓库根目录 `.env`，不能作为生产 Secret。 |
| [`docker-compose.production.yml`](docker-compose.production.yml) | 生产 Compose | 覆盖根 Compose 的域名、Production 环境、证书、持久化 Key 和回环端口；关闭 PostgreSQL、Collector 等宿主机端口。不能单独运行。 |
| [`Dockerfile.server-prebuilt`](Dockerfile.server-prebuilt) | 预构建 Server 制品 | 只包含 .NET Runtime，把已经 publish 的 Server 和 Migrations 装入非 root 镜像；不负责编译源码。 |
| [`Dockerfile.web-prebuilt`](Dockerfile.web-prebuilt) | 预构建 Web 制品 | 只包含 Node Runtime 和 Next.js Standalone 输出，以 `node` 用户运行，并移除运行时不需要的 npm/npx。 |

根目录 [`Dockerfile`](../Dockerfile) 和 [`Frontend/Dockerfile`](../Frontend/Dockerfile) 才是
默认 Compose 使用的源码构建文件；两个 `*-prebuilt` 文件只配合下面的预构建脚本使用。

### 6.2 Nginx

| 文件 | 作用 |
| --- | --- |
| [`Nginx/asterloom.bootstrap.conf`](Nginx/asterloom.bootstrap.conf) | 首次申请证书前的 HTTP 配置。仅放行 ACME Challenge，其余请求返回 503。 |
| [`Nginx/asterloom.conf`](Nginx/asterloom.conf) | 完整生产反向代理。负责 HTTP→HTTPS、TLS、Web、OIDC/JSON、原生 gRPC、参考应用和对象传输路由，并允许最大 2 GiB 请求体。 |

这两个文件由宿主机脚本复制到 `/etc/nginx/sites-available/`，不是挂载给容器的配置。

### 6.3 OpenTelemetry

| 文件 | 作用 |
| --- | --- |
| [`OpenTelemetry/otel-collector.yaml`](OpenTelemetry/otel-collector.yaml) | 开启 OTLP gRPC 4317、OTLP HTTP 4318 和健康检查 13133，使用 memory limiter 与 batch processor；当前只输出到 debug exporter。 |

### 6.4 Scripts

| 文件 | 平台 | 作用和副作用 |
| --- | --- | --- |
| [`Scripts/Prepare-ProductionHost.sh`](Scripts/Prepare-ProductionHost.sh) | Linux/root | 生成缺失的 `.env` 和 OpenIddict PFX、准备 Data Protection 目录、安装临时 Nginx Site。不会安装系统包，也不会启动 Compose。 |
| [`Scripts/Enable-ProductionTls.sh`](Scripts/Enable-ProductionTls.sh) | Linux/root | 使用 Certbot Webroot 获取证书，将 Nginx 切换到完整 HTTPS 配置。 |
| [`Scripts/Smoke-Test-Production.sh`](Scripts/Smoke-Test-Production.sh) | Linux | 对线上 HTTPS、Passport、OIDC、BFF、JSON API 和 Logout 做端到端冒烟测试。 |
| [`Scripts/Provision-Reference-App.sh`](Scripts/Provision-Reference-App.sh) | Linux | 通过真实 Web BFF 管理 API 创建参考 Tenant/Application/OIDC Client 和授权绑定，把新 Secret 写入 `.data/reference-app/reference.env`。会轮换 Secret 并修改平台数据。 |
| [`Scripts/Build-Server-Prebuilt.sh`](Scripts/Build-Server-Prebuilt.sh) | Linux | 在临时目录 publish Server/Migrations，并打包成可传输的 `.tar.gz`；输出 SHA-256 和大小。 |
| [`Scripts/Build-Web-Prebuilt.sh`](Scripts/Build-Web-Prebuilt.sh) | Linux/联网 | 下载并校验最新 Node 24 Linux x64，执行 `npm ci`/Next build，打包 Standalone Runtime 制品。 |
| [`Scripts/Build-Reference-DesktopUpdate.ps1`](Scripts/Build-Reference-DesktopUpdate.ps1) | PowerShell/Windows RID | 构建参考客户端基线和目标版本，调用 Velopack 生成 Setup、Full、Delta，并通过重建 Full 包及 SHA-256 验证差分包。输出目录必须不存在。 |
| [`Scripts/Sync-ProtocolArtifacts.ps1`](Scripts/Sync-ProtocolArtifacts.ps1) | PowerShell | 构建并临时启动 Development Server，下载 canonical OpenAPI，再用 Kiota 重建 `Frontend/lib/api/generated`。会修改需要提交的生成文件。 |

### 6.5 预构建制品用法

预构建脚本用于“在构建机编译、把结果搬到镜像构建机”的场景；当前默认 Compose 和 CI
不会自动调用它们。

Server 制品构建机需要兼容的 .NET 10 SDK、`realpath`、`tar` 和 `sha256sum`；Web 制品
构建机还需要联网访问 `nodejs.org`，并安装 `curl`、`grep`、`awk`、`xz`、`rsync` 和
`sha256sum`。目标镜像构建机只需要 Docker 和解压后的最小 Build Context。

```bash
Deploy/Scripts/Build-Server-Prebuilt.sh "$PWD" /tmp/asterloom-server.tar.gz
Deploy/Scripts/Build-Web-Prebuilt.sh "$PWD" /tmp/asterloom-web.tar.gz
```

解压后的目录本身就是最小 Docker Build Context，例如：

```bash
mkdir /tmp/asterloom-server-context
tar -xzf /tmp/asterloom-server.tar.gz -C /tmp/asterloom-server-context
docker build \
  -f /tmp/asterloom-server-context/Deploy/Dockerfile.server-prebuilt \
  -t asterloom-server:prebuilt \
  /tmp/asterloom-server-context
```

Web 制品使用相同方式，并选择 `Deploy/Dockerfile.web-prebuilt`。默认生产 Compose 仍引用
`asterloom-server:local` 和 `asterloom-web:local`，若采用预构建镜像发布，还需要在部署
流水线或额外 Compose Override 中显式替换镜像来源。

桌面更新构建脚本的完整打包、RID、上传和 Delta 更新流程见
[桌面自动更新指南](../Docs/Module/Desktop-Updates.zh-CN.md)。

## 7. 脚本产生但不提交的文件

| 路径 | 产生者 | 用途 |
| --- | --- | --- |
| `../.env` | 手动复制或 `Prepare-ProductionHost.sh` | Compose Secret 和 Bootstrap 配置。 |
| `Secrets/asterloom-signing.pfx` | `Prepare-ProductionHost.sh` | OpenIddict Token 签名证书。 |
| `Secrets/asterloom-encryption.pfx` | `Prepare-ProductionHost.sh` | OpenIddict Token 加密证书。 |
| `../.data/dataprotection-keys/` | `Prepare-ProductionHost.sh` / Server | 持久化 ASP.NET Core Data Protection Key。 |
| `../.data/reference-app/reference.env` | `Provision-Reference-App.sh` | 参考服务、原生客户端和业务 BFF 凭据。 |
| `../.data/reference-app/state/` | 参考客户端 | 参考应用可写诊断和 Provision 状态。 |

这些路径都包含 Secret 或运行状态，已在 `.gitignore` 中排除，禁止提交到仓库。
