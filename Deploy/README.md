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
`server` starts only after it succeeds. Open `http://localhost:60000` and use the
local-development-only administrator:

```text
Email:    admin@asterloom.local
Password: Asterloom-Local-Admin!2026
```

Local Compose reserves host ports `60000–60010`; `60000–60005` are currently
assigned and `60006–60010` remain reserved. Containers must use Compose service
names and container ports—for example, the Web BFF calls `http://server:8000`—
rather than host-published ports:

| Compose service / listener | Container port | Local host port | Purpose |
| --- | --- | --- | --- |
| `web` | `3000` | `60000` | Management UI and the `/api/auth/*` and `/api/asterloom/*` BFF routes. |
| `server` HTTP/JSON / Passport | `8000` | `60001` | HTTP/1.1, JSON Transcoding, OIDC/OAuth, and health endpoints. |
| `server` native gRPC | `8001` | `60002` | HTTP/2 native gRPC; browsers do not connect directly. |
| `reference-backend` HTTP/JSON | `5090` | `60004` | Reference application JSON Transcoding. |
| `reference-backend` native gRPC | `5091` | `60005` | Reference application HTTP/2 gRPC. |
| `postgres` | `5432` | not published | Compose-network only. |
| `redis` | `6379` | not published | Stores Web BFF sessions only inside the Compose network. |
| `minio` S3 API | `9000` | `60003` | S3-compatible object transfer. |
| `minio` Console | `9001` | not published | Compose-network only. |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | not published | Compose-network-only OpenTelemetry ingestion. |
| `otel-collector` ReferenceApp OTLP gRPC | `14317` | not published | Isolated ReferenceApp delivery-verification receiver. |
| `otel-collector` health | `13133` | not published | Compose-network only. |
| `otel-collector` internal metrics | `8888` | not published | Compose-network-only delivery verification. |
| `migrations` / `reference-client` | none | none | One-shot command containers with no network listener. |

Common lifecycle commands:

```powershell
docker compose logs -f server web
docker compose up -d --build
docker compose down
```

Compose stores local runtime state under the repository root `.data/` directory:
PostgreSQL in `.data/postgres/`, MinIO in `.data/minio/`, Redis in
`.data/redis/`, Data Protection keys in `.data/dataprotection-keys/`, and
reference application state under `.data/reference-app/`. `docker compose down`
and `docker compose down -v` do not remove these bind-mounted directories; delete
the relevant `.data/*` directory only when an intentional full reset is required.

### 2.3 Run the server and web console from source

The Development server can use in-memory persistence for fast Identity, BFF,
and management UI iteration. This mode does not validate the real PostgreSQL,
MinIO, Redis, or Collector integrations.
The `5080/3000` values in this section are direct-process development ports, not
container-to-host mappings from the table above.

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

The production domain is configured by `ASTERLOOM_DOMAIN` and defaults to
`asterloom.momiya.cloud`. The deployment runs Nginx on the host and application
services in containers:

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

Nginx does not run in a container. The production override binds application
ports only to `127.0.0.1`; Collector OTLP/HTTP is also loopback-only, while
PostgreSQL, Redis, and the MinIO Console have no host binding. Normally only
`80/tcp`, `443/tcp`, and the required SSH port should be publicly reachable.

The production container ports and host bindings are:

| Compose service / listener | Container port | Production host binding | Public entry point |
| --- | --- | --- | --- |
| `web` | `3000` | `127.0.0.1:60000` | Nginx default Web/BFF route. |
| `server` HTTP/JSON / Passport | `8000` | `127.0.0.1:60001` | Nginx HTTP upstream. |
| `server` native gRPC | `8001` | `127.0.0.1:60002` | Nginx `grpc_pass` upstream. |
| `minio` S3 API | `9000` | `127.0.0.1:60003` | `/asterloom-objects/*` presigned transfers. |
| `reference-backend` HTTP/JSON | `5090` | `127.0.0.1:60004` | `/api/reference/*`. |
| `reference-backend` native gRPC | `5091` | `127.0.0.1:60005` | Native ReferenceAppService gRPC route. |
| `minio` Console | `9001` | not published | Compose-network only; no public administration UI. |
| `postgres` | `5432` | not published | Compose-network only. |
| `redis` | `6379` | not published | Compose-network only. |
| `otel-collector` OTLP gRPC / HTTP | `4317` / `4318` | not published / `127.0.0.1:60006` | HTTP is available only through host loopback, for example through an SSH tunnel. |
| `otel-collector` ReferenceApp OTLP gRPC | `14317` | not published | Isolated ReferenceApp delivery-verification receiver. |
| `otel-collector` health | `13133` | not published | Compose-network only. |
| `otel-collector` internal metrics | `8888` | not published | Compose-network-only delivery verification. |
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

### 4.2 Configure the production domain

The root `.env` is the single persistent source for the public domain:

```dotenv
ASTERLOOM_DOMAIN=asterloom.momiya.cloud
CERTBOT_EMAIL=admin@asterloom.momiya.cloud
```

`ASTERLOOM_DOMAIN` must be a DNS hostname without a scheme, path, port, or
trailing slash. It drives the OIDC issuer and callbacks, public storage URL, Web
origin, reference client, Certbot request, smoke tests, and both Nginx templates.
The deployment scripts validate it before making host changes. `CERTBOT_EMAIL`
is optional and defaults to `admin@<ASTERLOOM_DOMAIN>`.

The production Compose override expands the same variable directly. Nginx does
not expand environment variables, so `Install-ProductionNginx.sh` renders the
checked-in `__ASTERLOOM_DOMAIN__` templates into the stable host site
`/etc/nginx/sites-available/asterloom`, validates it, and reloads Nginx. Do not
copy either template directly into `/etc/nginx`.

A process-level `ASTERLOOM_DOMAIN` or `CERTBOT_EMAIL` overrides `.env` for an
individual script/Compose invocation. Prefer recording production values in
`.env` so every subsequent command uses the same domain.

To change an existing deployment, first point the new DNS name at the host and
schedule a maintenance window. Update both values in `.env`, rerun
`Prepare-ProductionHost.sh` to install the new HTTP bootstrap site, run
`Enable-ProductionTls.sh`, and recreate the production Compose stack. The
migration/bootstrap service reconciles the built-in Web OIDC client's callback
and logout URIs with the new domain. Rerun `Provision-Reference-App.sh` when the
reference application is enabled. Because the public OIDC issuer changes,
existing users should sign in again.

### 4.3 Check out the repository and prepare secrets

```bash
sudo mkdir -p /home/Dev
sudo chown "$USER:$USER" /home/Dev
cd /home/Dev
git clone <repository-url> Asterloom
cd Asterloom
sudo bash Deploy/Scripts/Prepare-ProductionHost.sh
# For another domain, replace the command above with the following; the values
# are persisted in the newly generated .env:
# sudo ASTERLOOM_DOMAIN=identity.example.com CERTBOT_EMAIL=ops@example.com \
#   bash Deploy/Scripts/Prepare-ProductionHost.sh
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
| `ASTERLOOM_DOMAIN` | Public DNS hostname shared by Compose, Nginx, TLS, provisioning, and validation. Defaults to `asterloom.momiya.cloud`. |
| `CERTBOT_EMAIL` | ACME contact address. Defaults to `admin@<ASTERLOOM_DOMAIN>`. |
| `POSTGRES_PASSWORD` | PostgreSQL application account. |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | S3-compatible object storage. |
| `REDIS_PASSWORD` | BFF session Redis. |
| `ASTERLOOM_BOOTSTRAP_ADMIN_NAME` | Initial administrator display name. |
| `ASTERLOOM_BOOTSTRAP_ADMIN_EMAIL` / `ASTERLOOM_BOOTSTRAP_ADMIN_PASSWORD` | Initial administrator credentials. |
| `ASTERLOOM_OIDC_CLIENT_SECRET` | Web BFF confidential OIDC client secret. |
| `ASTERLOOM_SESSION_ENCRYPTION_KEY` | Base64-encoded 32-byte key that encrypts Redis sessions. |
| `TELEMETRY_INGESTION_API_KEY` | Shared secret used only between Collector and Server for OTLP database ingestion. |
| `ASTERLOOM_CERTIFICATE_PASSWORD` | Password for both OpenIddict PFX files; required by the production override. |

Do not use the local `.env.example` as production secrets. The root `.env`,
`Deploy/Secrets/`, and `.data/` are ignored by Git, but still require strict
permissions and secure secret/backup handling. Save the generated administrator
password from `.env` securely; the application does not display it again.

Compose configures the built-in Web OIDC client ID (`asterloom-web`) consistently for the migration/bootstrap,
server, and Web containers. Bootstrap persists a configuration-managed marker on this client. The Identity API and
Web Console therefore expose it as an immutable system resource; callback URLs and its secret are changed only via
deployment configuration followed by a migration/bootstrap run.

The token signing/encryption PFX files are separate from the HTTPS certificate.
The server uses the PFX files inside its container for tokens; Certbot creates
the HTTPS certificate used by host Nginx for TLS termination.

Production secrets and runtime state live at these host paths after preparation:

| Path | Contents |
| --- | --- |
| `<repo>/.env` | Production domain, database/Redis/MinIO passwords, bootstrap administrator, OIDC secret, session encryption key, telemetry ingestion key, and PFX password. |
| `<repo>/Deploy/Secrets/asterloom-signing.pfx` | OpenIddict token signing certificate. |
| `<repo>/Deploy/Secrets/asterloom-encryption.pfx` | OpenIddict token encryption certificate. |
| `<repo>/.data/postgres/` | PostgreSQL database files. |
| `<repo>/.data/minio/` | S3-compatible object data. |
| `<repo>/.data/redis/` | Redis append-only session data. |
| `<repo>/.data/dataprotection-keys/` | ASP.NET Core Data Protection key ring. |
| `<repo>/.data/reference-app/reference.env` | Reference application client/resource server credentials, when provisioned. |
| `<repo>/.data/reference-app/state/` | Reference application writable state, when used. |
| `/etc/letsencrypt/live/<domain>/` and `/etc/letsencrypt/archive/<domain>/` | HTTPS certificate managed by Certbot. |
| `/etc/nginx/sites-available/asterloom` and `/etc/nginx/sites-enabled/asterloom` | Rendered host Nginx site. |

### 4.4 Obtain the TLS certificate

After DNS has propagated and `/.well-known/acme-challenge/` is reachable over
HTTP, run:

```bash
sudo bash Deploy/Scripts/Enable-ProductionTls.sh
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
source Deploy/Scripts/Production-Domain.sh
load_asterloom_production_domain true
curl --fail "https://$ASTERLOOM_DOMAIN/health/live"
curl --fail "https://$ASTERLOOM_DOMAIN/health/ready"
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
  up -d --force-recreate otel-collector reference-backend

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

sudo bash Deploy/Scripts/Install-ProductionNginx.sh production

docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  build server web reference-backend
docker compose \
  -f docker-compose.yml \
  -f Deploy/docker-compose.production.yml \
  up -d --no-build --remove-orphans
sudo bash Deploy/Scripts/Smoke-Test-Production.sh
```

Building first lets the old containers continue serving traffic during the
build. `up` then recreates services from the new images and executes the one-shot
migration path before the server starts. Compose does not update host Nginx, so
port or route changes require rerunning `Install-ProductionNginx.sh production`;
a domain change must follow the bootstrap and certificate sequence in section
4.2. Do not use only `docker compose restart` for image or environment changes:
restart does not recreate containers.

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

- `.data/postgres/`;
- `.data/minio/`;
- `.data/redis/` if seamless BFF session continuity is required;
- the root `.env`;
- `Deploy/Secrets/*.pfx`;
- `.data/dataprotection-keys/`;
- `.data/reference-app/reference.env` and `state/` when using the reference app;
- the host's `/etc/letsencrypt/` and Nginx site configuration.

Redis primarily contains disposable BFF sessions. `docker compose down` and
`docker compose down -v` stop containers without deleting the bind-mounted
`.data/*` directories; remove those directories explicitly only as part of an
intentional reset. Database migrations are forward-applied, so evaluate schema
compatibility before rolling code back.

The Collector uses a shared ingestion key to forward traces, metrics, and logs to
the Server, which stores them in PostgreSQL `telemetry.records` for seven days.
No telemetry file volume is created; PostgreSQL backups must cover these records.

The Mail module opens outbound connections directly from the Server container
to SMTP hosts configured in the console; it adds no inbound or host-mapped
port. Production firewalls must allow the provider's outbound SMTP port
(commonly 465 or 587). SMTP authorization codes are protected by Data
Protection, so losing `.data/dataprotection-keys/` requires entering them again.

## 6. Files under `Deploy/`

### 6.1 Top-level files

| File | Used for | Purpose and notes |
| --- | --- | --- |
| [`README.md`](README.md) | Documentation | English version of this guide. |
| [`README.zh-CN.md`](README.zh-CN.md) | Documentation | Chinese version of this guide. |
| [`.env.example`](.env.example) | Local Compose / configuration reference | Example domain, ACME contact, local database, MinIO, Redis, administrator, OIDC, and session values. Copy it to the repository root as `.env` only for local development; never use its example secrets in production. |
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
| [`Nginx/asterloom.bootstrap.conf`](Nginx/asterloom.bootstrap.conf) | HTTP-only template used before certificate issuance. It serves ACME challenges and returns 503 for everything else. |
| [`Nginx/asterloom.conf`](Nginx/asterloom.conf) | Full production reverse-proxy template for HTTP-to-HTTPS, TLS, Web, OIDC/JSON, native gRPC, the reference app, and object transfer. It allows request bodies up to 2 GiB. |

`Install-ProductionNginx.sh` renders the domain placeholder and installs the
result as `/etc/nginx/sites-available/asterloom`. The templates are not mounted
into a container and must not be installed without rendering.

### 6.3 OpenTelemetry

| File | Purpose |
| --- | --- |
| [`OpenTelemetry/otel-collector.yaml`](OpenTelemetry/otel-collector.yaml) | Enables OTLP gRPC 4317, OTLP HTTP 4318, and health 13133, with a memory limiter, batch processor, and authenticated OTLP/JSON export to Server/PostgreSQL. |

### 6.4 Scripts

| File | Platform | Purpose and side effects |
| --- | --- | --- |
| [`Scripts/Prepare-ProductionHost.sh`](Scripts/Prepare-ProductionHost.sh) | Linux/root | Generates a missing `.env` and OpenIddict PFX files, prepares Data Protection storage, and installs the bootstrap Nginx site. It does not install packages or start Compose. |
| [`Scripts/Enable-ProductionTls.sh`](Scripts/Enable-ProductionTls.sh) | Linux/root | Obtains a certificate with Certbot webroot and switches Nginx to the full HTTPS configuration. |
| [`Scripts/Production-Domain.sh`](Scripts/Production-Domain.sh) | Linux/sourced helper | Loads `.env`, applies process-level overrides and defaults, validates `ASTERLOOM_DOMAIN` and `CERTBOT_EMAIL`, and exports them for deployment scripts. |
| [`Scripts/Install-ProductionNginx.sh`](Scripts/Install-ProductionNginx.sh) | Linux/root | Renders either Nginx domain template, installs the stable `asterloom` site, removes a legacy domain-named enabled symlink, validates Nginx, and reloads it. `--render` writes the result to stdout without host changes. |
| [`Scripts/Smoke-Test-Production.sh`](Scripts/Smoke-Test-Production.sh) | Linux | End-to-end production smoke test for HTTPS, Passport, OIDC, BFF, JSON API, and logout. |
| [`Scripts/Provision-Reference-App.sh`](Scripts/Provision-Reference-App.sh) | Linux | Uses the real Web BFF management APIs to create the reference tenant/application, business API scope/audience, bound Public Native Client, Confidential Clients, and authorization binding. It writes secrets and Reference Resource Server settings to `.data/reference-app/reference.env`, rotates secrets, and mutates platform data. |
| [`Scripts/Build-Server-Prebuilt.sh`](Scripts/Build-Server-Prebuilt.sh) | Linux | Publishes Server and Migrations in a temporary directory, creates a portable `.tar.gz`, and prints its SHA-256 and size. |
| [`Scripts/Build-Web-Prebuilt.sh`](Scripts/Build-Web-Prebuilt.sh) | Linux/network | Downloads and verifies the latest Node 24 Linux x64 release, runs `npm ci` and the Next build, and archives the Standalone runtime. |
| [`Scripts/Build-Reference-DesktopUpdate.ps1`](Scripts/Build-Reference-DesktopUpdate.ps1) | PowerShell/Windows RID | Builds baseline and target reference clients, asks Velopack for Setup/Full/Delta packages, reconstructs the target Full package, and verifies SHA-256. The output directory must not exist. |
| [`Scripts/New-VelopackSigningBundle.ps1`](Scripts/New-VelopackSigningBundle.ps1) | PowerShell/offline signer or CI | Signs the lowercase SHA-256 text for one or more Velopack Full/Delta packages with an RSA-PSS private key and writes the private-key-free `signing-metadata.json` used by Web quick upload. |
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
| `../.data/postgres/` | Compose / PostgreSQL | PostgreSQL database files. |
| `../.data/minio/` | Compose / MinIO | S3-compatible object data. |
| `../.data/redis/` | Compose / Redis | Redis append-only Web BFF session data. |
| `../.data/dataprotection-keys/` | `Prepare-ProductionHost.sh` / Server | Persistent ASP.NET Core Data Protection keys. |
| `../.data/reference-app/reference.env` | `Provision-Reference-App.sh` | Reference service/BFF credentials, Public Client ID/API scope, and Reference Backend issuer/audience/tenant/application validation settings. |
| `../.data/reference-app/state/` | Reference client | Writable reference provisioning and diagnostic state. |

These paths contain secrets or runtime state. They are excluded by `.gitignore`
and must never be committed.
