# Asterloom development and deployment guide

[English](README.md) | [简体中文](README.zh-CN.md)

This guide explains local development, the current production topology, and the
purpose of every file under `Deploy/`. For module APIs and SDK usage, see the
[module guides](../Docs/Module/README.md).

## 1. Choose a workflow

| Scenario | Recommended workflow | Main files |
| --- | --- | --- |
| Start the complete local stack | Docker Compose | `../docker-compose.yml`, `.env.example` |
| Iterate quickly on the server or web console | Run from source | `global.json`, `Frontend/package.json` |
| Deploy the current production topology | Compose plus host Nginx | `../docker-compose.yml`, `docker-compose.production.yml`, `Nginx/*`, host scripts |
| Produce portable prebuilt artifacts | Prebuilt scripts | `Build-Server-Prebuilt.sh`, `Build-Web-Prebuilt.sh`, the two `Dockerfile.*-prebuilt` files |
| Validate the reference application or desktop updates | Reference scripts | `Provision-Reference-App.sh`, `Build-Reference-DesktopUpdate.ps1` |

The production Compose file is an override for the root Compose file. It cannot
be used by itself. Pass both files, in this order, for every production command:

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  <command>
```

## 2. Local development

### 2.1 Prerequisites

- Complete container stack: Docker Engine or Docker Desktop and Docker Compose v2.
- Source workflow: .NET SDK 10.0.400 as selected by
  [`global.json`](../global.json), and Node.js 24 or newer.
- Protocol changes also require PowerShell 7 and the repository's .NET local tools.

### 2.2 Start the complete stack with Docker Compose

Run from the repository root:

```powershell
Copy-Item Deploy/.env.example .env
docker compose up --build -d
docker compose ps -a
docker compose logs -f migrations server web
```

On Linux or macOS, replace the first command with:

```bash
cp Deploy/.env.example .env
```

`migrations` is a one-shot container. An `Exited (0)` status is expected; the
`server` starts only after it succeeds. Open `http://localhost:3000` and use the
local-development-only administrator:

```text
Email:    admin@asterloom.local
Password: Asterloom-Local-Admin!2026
```

The local ports are listed below. Containers must use Compose service names and
container ports—for example, the Web BFF calls `http://server:8080`—rather than
host-published ports:

| Compose service / listener | Container port | Local host port | Purpose |
| --- | --- | --- | --- |
| `web` | `3000` | `3000` | Management UI and the `/api/auth/*` and `/api/asterloom/*` BFF routes. |
| `server` HTTP/JSON / Passport | `8080` | `5080` | HTTP/1.1, JSON Transcoding, OIDC/OAuth, and health endpoints. |
| `server` native gRPC | `8081` | `5081` | HTTP/2 native gRPC; browsers do not connect directly. |
| `reference-backend` HTTP/JSON | `5090` | `5090` | Reference application JSON Transcoding. |
| `reference-backend` native gRPC | `5091` | `5091` | Reference application HTTP/2 gRPC. |
| `postgres` | `5432` | `5432` | PostgreSQL. |
| `redis` | `6379` | not published | Stores Web BFF sessions only inside the Compose network. |
| `minio` S3 API | `9000` | `9000` | S3-compatible object transfer. |
| `minio` Console | `9001` | `9001` | MinIO administration. |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | `4317` / `4318` | OpenTelemetry ingestion. |
| `otel-collector` health | `13133` | `13133` | Collector health endpoint. |
| `migrations` / `reference-client` | none | none | One-shot command containers with no network listener. |

Common lifecycle commands:

```powershell
docker compose logs -f server web
docker compose up -d --build
docker compose down
```

`docker compose down` preserves named volumes. `docker compose down -v` removes
local PostgreSQL, MinIO, Redis, and Data Protection data; use it only when an
intentional full reset is required.

### 2.3 Run the server and web console from source

The Development server can use in-memory persistence for fast Identity, BFF,
and management UI iteration. This mode does not validate the real PostgreSQL,
MinIO, Redis, or Collector integrations.

In the first PowerShell terminal:

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

In the second PowerShell terminal:

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

The memory session store is forbidden in production. Use Compose for complete
integration work: it stores encrypted server-side BFF sessions in Redis and
connects the server to PostgreSQL and S3-compatible storage.

### 2.4 Development checks

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

After changing a `.proto` file, also run:

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1 -Configuration Release
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

Commit the regenerated OpenAPI document and Kiota client with the source change.

## 3. Current production topology

The checked-in production assets target `asterloom.kirayuukiasuna.cloud`. They
run Nginx on the host and the application services in containers:

```text
Internet :80/:443
  └─ host Nginx
       ├─ Web / BFF                    → 127.0.0.1:15081
       ├─ Asterloom HTTP/JSON/OIDC     → 127.0.0.1:15080
       ├─ Asterloom native gRPC        → 127.0.0.1:15084
       ├─ Reference HTTP/JSON           → 127.0.0.1:15082
       ├─ Reference native gRPC         → 127.0.0.1:15083
       └─ signed object transfer        → 127.0.0.1:19000

Docker network only
  ├─ PostgreSQL
  ├─ Redis
  └─ OpenTelemetry Collector
```

Nginx does not run in a container. The production override binds application
ports only to `127.0.0.1`; PostgreSQL, Redis, the Collector, and the MinIO Console
are not directly exposed to the host or Internet. Normally only `80/tcp`,
`443/tcp`, and the required SSH port should be publicly reachable.

The production container ports and host bindings are:

| Compose service / listener | Container port | Production host binding | Public entry point |
| --- | --- | --- | --- |
| `web` | `3000` | `127.0.0.1:15081` | Nginx default Web/BFF route. |
| `server` HTTP/JSON / Passport | `8080` | `127.0.0.1:15080` | Nginx HTTP upstream. |
| `server` native gRPC | `8081` | `127.0.0.1:15084` | Nginx `grpc_pass` upstream. |
| `reference-backend` HTTP/JSON | `5090` | `127.0.0.1:15082` | `/api/reference/*`. |
| `reference-backend` native gRPC | `5091` | `127.0.0.1:15083` | Native ReferenceAppService gRPC route. |
| `minio` S3 API | `9000` | `127.0.0.1:19000` | `/asterloom-objects/*` presigned transfers. |
| `minio` Console | `9001` | not published | Compose-network only; no public administration UI. |
| `postgres` | `5432` | not published | Compose-network only. |
| `redis` | `6379` | not published | Compose-network only. |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | not published | Compose-network only. |
| `otel-collector` health | `13133` | not published | Compose-network only. |
| `migrations` / `reference-client` | none | none | One-shot containers with no listener. |

“Not published” means only that no host port exists. Containers on the Compose
network can still connect to `<service-name>:<container-port>`, such as
`postgres:5432`, `minio:9000`, and `otel-collector:4317` from the server.

The public route map is:

| Public path | Upstream | Purpose |
| --- | --- | --- |
| `/.well-known/*`, `/connect/*`, `/passport/*` | Server HTTP | OIDC discovery, OAuth endpoints, and Passport pages. |
| `/api/v1/*`, `/health/*` | Server HTTP | JSON Transcoding APIs and health endpoints. |
| `/asterloom.<service>/<method>` | Server gRPC | Native gRPC. |
| `/api/reference/*` | Reference HTTP | Reference backend JSON Transcoding. |
| `/asterloom.reference.v1.ReferenceAppService/*` | Reference gRPC | Reference backend native gRPC. |
| `/asterloom-objects/*` | MinIO | S3 presigned uploads and downloads. |
| All other paths | Web | Next.js Console and BFF. |

## 4. First production deployment

### 4.1 Host prerequisites

The current scripts target a systemd-based Linux host and assume these tools
are already installed:

- Docker Engine and the Compose v2 plugin;
- Nginx, Certbot, and OpenSSL;
- Git, Bash, curl, jq, sed, and awk;
- a domain pointing to the host, with inbound ports 80 and 443 reachable.

The scripts do not install system packages or configure DNS or cloud firewalls.
The operator must also be authorized to access the Docker daemon; otherwise use
`sudo` for Docker commands according to the host policy.

### 4.2 Domain customization boundary

The current production domain appears in:

- `docker-compose.production.yml`: issuer, OIDC callbacks, and public storage URL;
- `Nginx/asterloom.bootstrap.conf` and `Nginx/asterloom.conf`: `server_name`
  and TLS paths;
- `Scripts/Prepare-ProductionHost.sh`: Nginx site name and default admin email;
- `Scripts/Enable-ProductionTls.sh`: Certbot domain, certificate name, and default
  contact email.

Update all of those consistently before deploying to another domain.
`Provision-Reference-App.sh` and `Smoke-Test-Production.sh` accept a temporary
`ASTERLOOM_DOMAIN` override, but it does not rewrite Compose or Nginx settings.

### 4.3 Check out the repository and prepare secrets

```bash
sudo mkdir -p /home/Dev
sudo chown "$USER:$USER" /home/Dev
cd /home/Dev
git clone <repository-url> Asterloom
cd Asterloom
sudo bash Deploy/Scripts/Prepare-ProductionHost.sh
```

`Prepare-ProductionHost.sh`:

1. generates production secrets only when the root `.env` does not exist;
2. creates the two token signing/encryption PFX files used by OpenIddict;
3. creates the persistent Data Protection directory and grants container UID
   1654 access;
4. installs an ACME-challenge-only Nginx site, validates Nginx, and reloads it.

It does not overwrite an existing `.env` or PFX file. If `.env` already exists,
securely define at least these values:

| Variable | Purpose |
| --- | --- |
| `POSTGRES_PASSWORD` | PostgreSQL application account. |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | S3-compatible object storage. |
| `REDIS_PASSWORD` | BFF session Redis. |
| `ASTERLOOM_BOOTSTRAP_ADMIN_NAME` | Initial administrator display name. |
| `ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL` / `ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD` | Initial administrator credentials. |
| `ASTERLOOM_OIDC_CLIENT_SECRET` | Web BFF confidential OIDC client secret. |
| `ASTERLOOM_SESSION_ENCRYPTION_KEY` | Base64-encoded 32-byte key that encrypts Redis sessions. |
| `ASTERLOOM_CERTIFICATE_PASSWORD` | Password for both OpenIddict PFX files; required by the production override. |

Do not use the local `.env.example` as production secrets. The root `.env`,
`Deploy/Secrets/`, and `.data/` are ignored by Git, but still require strict
permissions and secure secret/backup handling. Save the generated administrator
password from `.env` securely; the application does not display it again.

The token signing/encryption PFX files are separate from the HTTPS certificate.
The server uses the PFX files inside its container for tokens; Certbot creates
the HTTPS certificate used by host Nginx for TLS termination.

### 4.4 Obtain the TLS certificate

After DNS has propagated and `/.well-known/acme-challenge/` is reachable over
HTTP, run:

```bash
sudo CERTBOT_EMAIL=admin@example.com \
  bash Deploy/Scripts/Enable-ProductionTls.sh
```

The script uses Certbot's webroot mode to obtain or reuse a certificate and then
replaces the bootstrap site with the full HTTPS proxy. Verify automatic renewal
and Nginx reload behavior for the host distribution:

```bash
sudo certbot renew --dry-run
sudo nginx -t
```

### 4.5 Build and start the application

```bash
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --build --remove-orphans
```

Compose starts PostgreSQL, runs the one-shot `migrations` service, and starts the
server only after migration succeeds. `Asterloom.Server` never applies production
schema changes implicitly during startup.

Inspect status and application logs:

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

Nginx is a host service, not a Compose service:

```bash
sudo systemctl status nginx
sudo tail -n 200 /var/log/nginx/asterloom.error.log
```

### 4.6 Validate the deployment

```bash
curl --fail https://asterloom.kirayuukiasuna.cloud/health/live
curl --fail https://asterloom.kirayuukiasuna.cloud/health/ready
sudo bash Deploy/Scripts/Smoke-Test-Production.sh
```

The smoke test uses the administrator in `.env` to exercise HTTPS, Passport,
OIDC, the Web BFF session, an authenticated JSON API, and logout. It requires
curl, jq, sed, and awk.

The full-capability reference application is optional. After the core platform
is healthy, run:

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

The provisioning script creates or updates reference OIDC clients and rotates
two client secrets. Recreate `reference-backend` afterward so it loads the new
secret. See the [reference application guide](../Docs/Reference-Application.md)
for the full behavior.

## 5. Subsequent releases and operations

### 5.1 Deploy an update

```bash
cd /home/Dev/Asterloom
git pull --ff-only
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --build --remove-orphans
sudo bash Deploy/Scripts/Smoke-Test-Production.sh
```

`up` recreates services for new images and executes the one-shot migration path
before the server starts. Do not use only `docker compose restart` for image or
environment changes: restart does not recreate containers.

### 5.2 Troubleshooting commands

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

A useful order is: verify `migrations`, verify `/health/ready`, verify that the
Web BFF can create a Redis session, verify the Nginx upstream listeners, and only
then investigate browser behavior.

### 5.3 Persistent data and backups

At minimum, back up or have a tested reconstruction plan for:

- PostgreSQL data;
- MinIO object data;
- the root `.env`;
- `Deploy/Secrets/*.pfx`;
- `.data/dataprotection-keys/`;
- `.data/reference-app/reference.env` and `state/` when using the reference app;
- the host's `/etc/letsencrypt/` and Nginx site configuration.

Redis primarily contains disposable BFF sessions, but include it in recovery if
seamless session continuity is required. Never use `docker compose down -v` as a
routine stop command; it removes production named volumes. Database migrations
are forward-applied, so evaluate schema compatibility before rolling code back.

The current Collector uses only the `debug` exporter. It writes traces, metrics,
and logs to Collector output and is not a durable observability backend. Update
`OpenTelemetry/otel-collector.yaml` and provide the required network and secrets
when connecting OTLP, Prometheus, Loki, or another backend.

## 6. Files under `Deploy/`

### 6.1 Top-level files

| File | Used for | Purpose and notes |
| --- | --- | --- |
| [`README.md`](README.md) | Documentation | English version of this guide. |
| [`README.zh-CN.md`](README.zh-CN.md) | Documentation | Chinese version of this guide. |
| [`.env.example`](.env.example) | Local Compose | Example local database, MinIO, Redis, administrator, OIDC, and session values. Copy it to the repository root as `.env`; never use it as production secrets. |
| [`docker-compose.production.yml`](docker-compose.production.yml) | Production Compose | Overrides the root Compose domain, Production environment, certificates, persistent keys, and loopback ports; removes direct PostgreSQL and Collector host ports. It cannot run alone. |
| [`Dockerfile.server-prebuilt`](Dockerfile.server-prebuilt) | Prebuilt server artifact | Runtime-only .NET image that copies already-published Server and Migrations output and runs as a non-root user. It does not compile source. |
| [`Dockerfile.web-prebuilt`](Dockerfile.web-prebuilt) | Prebuilt web artifact | Runtime-only Node image for Next.js Standalone output; runs as `node` and removes npm/npx from the runtime image. |

The root [`Dockerfile`](../Dockerfile) and
[`Frontend/Dockerfile`](../Frontend/Dockerfile) are the source-build files used
by default Compose. The two `*-prebuilt` files are used only with the prebuilt
scripts below.

### 6.2 Nginx

| File | Purpose |
| --- | --- |
| [`Nginx/asterloom.bootstrap.conf`](Nginx/asterloom.bootstrap.conf) | HTTP-only site used before certificate issuance. It serves ACME challenges and returns 503 for everything else. |
| [`Nginx/asterloom.conf`](Nginx/asterloom.conf) | Full production reverse proxy for HTTP-to-HTTPS, TLS, Web, OIDC/JSON, native gRPC, the reference app, and object transfer. It allows request bodies up to 2 GiB. |

The host scripts copy these files into `/etc/nginx/sites-available/`; they are not
mounted into a container.

### 6.3 OpenTelemetry

| File | Purpose |
| --- | --- |
| [`OpenTelemetry/otel-collector.yaml`](OpenTelemetry/otel-collector.yaml) | Enables OTLP gRPC 4317, OTLP HTTP 4318, and health 13133, with a memory limiter and batch processor. It currently exports only to `debug`. |

### 6.4 Scripts

| File | Platform | Purpose and side effects |
| --- | --- | --- |
| [`Scripts/Prepare-ProductionHost.sh`](Scripts/Prepare-ProductionHost.sh) | Linux/root | Generates a missing `.env` and OpenIddict PFX files, prepares Data Protection storage, and installs the bootstrap Nginx site. It does not install packages or start Compose. |
| [`Scripts/Enable-ProductionTls.sh`](Scripts/Enable-ProductionTls.sh) | Linux/root | Obtains a certificate with Certbot webroot and switches Nginx to the full HTTPS configuration. |
| [`Scripts/Smoke-Test-Production.sh`](Scripts/Smoke-Test-Production.sh) | Linux | End-to-end production smoke test for HTTPS, Passport, OIDC, BFF, JSON API, and logout. |
| [`Scripts/Provision-Reference-App.sh`](Scripts/Provision-Reference-App.sh) | Linux | Uses the real Web BFF management APIs to create the reference tenant/application/OIDC clients and authorization binding, then writes new secrets to `.data/reference-app/reference.env`. It rotates secrets and mutates platform data. |
| [`Scripts/Build-Server-Prebuilt.sh`](Scripts/Build-Server-Prebuilt.sh) | Linux | Publishes Server and Migrations in a temporary directory, creates a portable `.tar.gz`, and prints its SHA-256 and size. |
| [`Scripts/Build-Web-Prebuilt.sh`](Scripts/Build-Web-Prebuilt.sh) | Linux/network | Downloads and verifies the latest Node 24 Linux x64 release, runs `npm ci` and the Next build, and archives the Standalone runtime. |
| [`Scripts/Build-Reference-DesktopUpdate.ps1`](Scripts/Build-Reference-DesktopUpdate.ps1) | PowerShell/Windows RID | Builds baseline and target reference clients, asks Velopack for Setup/Full/Delta packages, reconstructs the target Full package, and verifies SHA-256. The output directory must not exist. |
| [`Scripts/Sync-ProtocolArtifacts.ps1`](Scripts/Sync-ProtocolArtifacts.ps1) | PowerShell | Builds and temporarily starts the Development server, downloads canonical OpenAPI, and rebuilds `Frontend/lib/api/generated` with Kiota. It changes generated files that must be committed. |

### 6.5 Prebuilt artifact workflow

The prebuilt scripts support building on one machine and moving minimal runtime
contexts to an image builder. Default Compose and the current CI do not invoke
them automatically.

The server artifact builder needs a compatible .NET 10 SDK, `realpath`, `tar`,
and `sha256sum`. The web artifact builder also needs network access to
`nodejs.org` and `curl`, `grep`, `awk`, `xz`, `rsync`, and `sha256sum`. The target
image builder needs only Docker and the extracted minimal build context.

```bash
Deploy/Scripts/Build-Server-Prebuilt.sh "$PWD" /tmp/asterloom-server.tar.gz
Deploy/Scripts/Build-Web-Prebuilt.sh "$PWD" /tmp/asterloom-web.tar.gz
```

Each extracted archive is a minimal Docker build context. For example:

```bash
mkdir /tmp/asterloom-server-context
tar -xzf /tmp/asterloom-server.tar.gz -C /tmp/asterloom-server-context
docker build \
  -f /tmp/asterloom-server-context/Deploy/Dockerfile.server-prebuilt \
  -t asterloom-server:prebuilt \
  /tmp/asterloom-server-context
```

Build the web artifact the same way with `Deploy/Dockerfile.web-prebuilt`.
Default production Compose still refers to `asterloom-server:local` and
`asterloom-web:local`; a prebuilt-image release must replace those image sources
in the deployment pipeline or an additional Compose override.

For the complete desktop update packaging, RID, upload, and Delta workflow, see
the [desktop update guide](../Docs/Module/Desktop-Updates.md).

## 7. Generated files that must not be committed

| Path | Created by | Purpose |
| --- | --- | --- |
| `../.env` | Manual copy or `Prepare-ProductionHost.sh` | Compose secrets and bootstrap settings. |
| `Secrets/asterloom-signing.pfx` | `Prepare-ProductionHost.sh` | OpenIddict token signing certificate. |
| `Secrets/asterloom-encryption.pfx` | `Prepare-ProductionHost.sh` | OpenIddict token encryption certificate. |
| `../.data/dataprotection-keys/` | `Prepare-ProductionHost.sh` / Server | Persistent ASP.NET Core Data Protection keys. |
| `../.data/reference-app/reference.env` | `Provision-Reference-App.sh` | Reference service, native client, and business BFF credentials. |
| `../.data/reference-app/state/` | Reference client | Writable reference provisioning and diagnostic state. |

These paths contain secrets or runtime state. They are excluded by `.gitignore`
and must never be committed.
