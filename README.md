# Asterloom

[English](README.md) | [简体中文](README.zh-CN.md)

Asterloom is a unified foundation platform for global Passport accounts, application membership,
identity, authorization, feature
flags, targeting, dynamic configuration, desktop releases, analytics,
telemetry, transactional email, RPC/HTTP, object storage, and persistence.

## Documentation

- [Development and deployment guide](Deploy/README.md)
- [开发与部署指南（中文）](Deploy/README.zh-CN.md)
- [Architecture and implementation baseline](Docs/Architecture.md)
- [Module-by-module guides](Docs/Module/README.md)
- [模块使用文档（中文）](Docs/Module/README.zh-CN.md)
- [Business application Passport integration](Docs/Module/Identity-Business-Integration.md)
- [业务应用统一账号接入（中文）](Docs/Module/Identity-Business-Integration.zh-CN.md)
- [Feature usage guide](Docs/Feature-Guide.md)
- [功能使用指南（中文）](Docs/Feature-Guide.zh-CN.md)
- [Desktop update packaging and release guide](Docs/Module/Desktop-Updates.md)
- [桌面自动更新指南（中文）](Docs/Module/Desktop-Updates.zh-CN.md)
- [File storage integration guide](Docs/Module/File-Storage.md)
- [文件存储指南（中文）](Docs/Module/File-Storage.zh-CN.md)
- [Transactional email guide](Docs/Module/Mail.md)
- [事务型应用邮件指南（中文）](Docs/Module/Mail.zh-CN.md)
- [Full-capability reference application](Docs/Reference-Application.md)
- [Standard protocol endpoints](Docs/Protocol/standard-endpoints.md)

## Repository

- `Backend`: .NET 10 server, MailKit delivery, modules, C# SDKs, and tests.
- `Frontend`: Next.js management console and BFF.
- `Proto/Asterloom`: versioned Protobuf API contracts.
- `Docs/Protocol`: generated OpenAPI and API/UI coverage data.
- [`Deploy`](Deploy/README.md): local and production deployment assets and their operating guide.

## Development

The commands below cover the shortest source workflow. For prerequisites,
Docker-based development, the complete port map, data reset behavior, and
troubleshooting, see the [development and deployment guide](Deploy/README.md).

```powershell
dotnet restore Backend/Asterloom.sln
dotnet build Backend/Asterloom.sln
dotnet test Backend/Asterloom.sln
```

For a dependency-free local Identity/BFF run, start the server with the
in-memory provider and an explicit development Passport client:

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

Then run the management console at `http://localhost:3000` in a second shell:

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

Sign in with `admin@asterloom.local` and the local-only password shown above.
Use unique secrets and the Redis session store outside development.

The browser calls the Next.js BFF under `/api/asterloom/*`; the BFF calls
Asterloom.Server's gRPC JSON Transcoding routes. Native .NET callers use gRPC.
The browser receives only an opaque HttpOnly session ID; OIDC tokens remain in
the BFF.

For an end-user native product, its Public Client signs in with Authorization
Code + PKCE and sends the resulting Access Token to its own business API. The
business API uses `Asterloom.Sdk.Identity.AspNetCore` to validate JWT/JWKS,
audience, and application binding, with optional real-time Asterloom permission
checks. Confidential Client credentials remain backend-only for registration
and service operations.

## Protocol workflow

`Proto/Asterloom` is the only API contract source. After changing a `.proto`
file, regenerate OpenAPI and the internal TypeScript client:

```powershell
./Deploy/Scripts/Sync-ProtocolArtifacts.ps1
dotnet run --project Backend/Tools/Asterloom.ApiCoverage -- --repo-root .
```

The coverage verifier fails unless every custom RPC has an HTTP mapping and
every Admin RPC has a permission, existing UI route, UI action marker, and E2E
test in `Docs/Protocol/admin-api-coverage.yaml`.

## Local stack

Copy `Deploy/.env.example` to `.env` if you want to override local
credentials, then start the server, web console, PostgreSQL, S3-compatible
storage, Redis BFF session store, and OpenTelemetry Collector:

```powershell
docker compose up --build
```

Open `http://localhost:60000`. Compose host ports are reserved in the
`60000–60010` range; see the [complete port map](Deploy/README.md#3-current-production-topology).
The example credentials are for local development only. The earlier
`5080/3000` source workflow uses direct-process development ports and is not a
container-to-host mapping.

Production schema changes are never run implicitly by Asterloom.Server. Run
the dedicated migration executable as a deployment step with
`Persistence:Provider=PostgreSql` and `ConnectionStrings:Asterloom` configured.
The Compose stack models this as a one-shot `migrations` service that must
complete before the server starts.

## Production deployment

Production combines the root Compose file with
`Deploy/docker-compose.production.yml`, while Nginx and TLS run on the host.
The public domain is configured once through `ASTERLOOM_DOMAIN` in the root
`.env` and defaults to `asterloom.momiya.cloud`. Production also requires
generated secrets, explicit migrations, persistent cryptographic keys, and
post-deployment smoke tests. Follow the
[production deployment procedure and per-file reference](Deploy/README.md)
instead of running the production override by itself.
