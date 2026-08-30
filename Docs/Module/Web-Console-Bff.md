# Web Console / BFF: administration and the browser security boundary

[English](Web-Console-Bff.md) | [简体中文](Web-Console-Bff.zh-CN.md) | [Module index](README.md)

The Web Console is the browser administration surface for every Asterloom Admin API. It also contains a Next.js
BFF (Backend for Frontend): the browser never owns OIDC tokens. The same-origin Next.js server manages the login
session and safely forwards requests to Asterloom Server.

## 1. Request path

```text
Browser
  ├─ /api/auth/*                 -> Next.js OIDC login/callback/logout
  └─ /api/asterloom/api/v1/*     -> Next.js BFF
                                        ├─ read HttpOnly session ID
                                        ├─ load and decrypt token session from Redis
                                        ├─ validate Origin + X-CSRF-Token
                                        ├─ refresh an expiring access token
                                        └─ Authorization: Bearer ...
                                              -> Asterloom JSON Transcoding /api/v1/*
```

The Next.js BFF is not another business backend. Protobuf contracts, gRPC services, and domain modules still own the
rules exactly once. The BFF only handles browser authentication, sessions, CSRF, header narrowing, token refresh,
and a same-origin proxy.

Native C# applications use gRPC/C# SDKs directly and do not pass through the BFF. Trusted external services can call
HTTPS `/api/v1/*` directly; only the browser console uses `/api/asterloom/*`.

## 2. Login and sessions

The Web Console uses OIDC Authorization Code + PKCE:

1. `/api/auth/login` creates state, nonce, verifier/challenge, and a ten-minute login transaction.
2. The browser redirects to Passport `/connect/authorize`.
3. `/api/auth/callback` validates state/nonce, exchanges the code, and creates a BFF session lasting at most eight hours.
4. The browser receives only a random HttpOnly, SameSite=Lax, Secure-on-HTTPS session cookie.
5. Thirty seconds before access-token expiry, the BFF refreshes it; a distributed lock serializes concurrent refreshes.
6. Logout deletes the server session, clears the cookie, and redirects through the Passport end-session endpoint.

Access, refresh, and ID tokens are never stored in Local Storage or exposed to browser JavaScript.

## 3. Why Redis exists

Redis is exclusively Web BFF session infrastructure, not an Asterloom domain store:

- Next.js instances share sessions, preserving login across rolling restarts.
- Login transactions and sessions have TTLs rather than depending on one process.
- Refresh locks prevent concurrent reuse/rotation of refresh tokens.
- The cookie is only a random ID; Redis keys use the ID's SHA-256 digest.
- Redis session payloads are additionally encrypted with a separate 256-bit AES-GCM key.

The Web uses the Node.js npm package `redis`, not a C# library. The current C# backend does not read Redis and has no
StackExchange.Redis dependency; C# domain stores use Npgsql with PostgreSQL. If C# later needs Redis, define a separate
purpose and contract—typically implemented with `StackExchange.Redis`—and never let Server or UI bypass public APIs
to inspect BFF sessions.

The `memory` store is only for development/tests, loses sessions at process exit, and cannot scale across instances.
Production configuration rejects it. See [ADR 0001](../ADR/0001-redis-for-web-bff-sessions.md).

## 4. CSRF, proxying, and errors

Every method other than GET/HEAD/OPTIONS must satisfy both:

- `Origin` exactly matches `ASTERLOOM_WEB_ORIGIN`.
- `X-CSRF-Token` matches the random value in the server session using constant-time comparison.

The BFF forwards only `Accept`, `Content-Type`, `If-Match`, `If-None-Match`, and `X-Request-ID`, then injects the bearer
token. Upstream requests time out after 30 seconds; an initial token-related 401 is retried once after refresh. A
failed Server fetch returns 502 `BACKEND_UNAVAILABLE`; refresh-time Session/Passport failure returns 503
`SESSION_SERVICE_UNAVAILABLE`; a missing session returns 401; and CSRF rejection returns 403. Passport failure in the
login callback redirects to the login page with an error code. If Redis throws while initially loading a session, the
current route can still surface a generic Next.js 500; inspect Web-side Redis logs instead of treating it as a domain
API failure.

When a page says `An unexpected error occurred.`, do more than refresh:

1. Inspect the failed `/api/asterloom/*` Network request and retain status, structured code, and `X-Request-ID`.
2. For 401 inspect session/issuer; for 403 inspect permission, Origin, and CSRF; for 409 re-read the resource version;
   for 502 inspect BFF-to-Server networking; for 503 inspect Redis/Passport.
3. Correlate the request ID in Server logs, [Audit](Audit.md), and [Telemetry](Telemetry.md).

## 5. Management coverage

The Web Console currently provides:

| Capability | Routes |
| --- | --- |
| Platform | `/tenants` |
| Identity | `/identity/users` |
| Authorization | `/authorization/roles` |
| Targeting | `/targeting/segments` |
| Feature | `/features` |
| Config | `/config` |
| Release | `/channels`, `/artifacts`, `/releases` |
| Analytics | `/analytics/schemas`, `/analytics/explorer` |
| Telemetry | `/telemetry/sources`, `/telemetry/health` |
| Storage | `/storage/buckets`, `/storage/objects` |
| Audit | `/audit` |
| Operations | `/operations/apis`, `/operations/health` |

`Backend/Tools/Asterloom.ApiCoverage` compares every Admin RPC with its permission, Web route, `data-ui-action`, and
E2E inventory. A new Admin API must ship with its page and test in the same change; a backend-only half implementation
is not considered complete.

## 6. UI, theme, and language

The stack is React 19, Next.js 16 App Router, TypeScript, Tailwind CSS 4, shadcn-ui/Radix, Lucide, Geist, SWR,
Zustand, Zod, Kiota, Sonner, and cmdk.

- Theme choices are `system`, `light`, and `dark`, stored under `asterloom-theme` and resolved before first paint.
- Languages are `en` and `zh-CN`; an explicit choice wins, followed by the browser language, and persists to storage/cookie.
- New pages must verify Card, border, input, table, badge, toast, loading, empty, and error states in both themes.
- All user-facing text belongs in the translation catalog; dates, numbers, and sizes use the active locale.

## 7. Configuration and deployment

Primary environment variables:

| Variable | Meaning |
| --- | --- |
| `ASTERLOOM_BACKEND_URL` | Internal URL used by BFF to reach Asterloom Server |
| `ASTERLOOM_PASSPORT_PUBLIC_URL` | Public Passport URL used in browser redirects |
| `ASTERLOOM_WEB_ORIGIN` | Exact public Web origin and CSRF origin |
| `ASTERLOOM_OIDC_ISSUER` | Must exactly match the token `iss` |
| `ASTERLOOM_OIDC_CLIENT_ID/SECRET` | Confidential Web client credentials |
| `ASTERLOOM_SESSION_STORE` | `memory` in development; `redis` required in production |
| `ASTERLOOM_SESSION_REDIS_URL/PASSWORD` | Internal Redis URL and password |
| `ASTERLOOM_SESSION_ENCRYPTION_KEY` | Base64-encoded 32-byte AES key |

Web Origin, Passport URL, and Issuer must use HTTPS in production. Inject secrets and encryption keys through a secret
manager. Nginx should route `/` to Web; `/connect`, `/.well-known`, `/passport`, `/api/v1`, and `/health` to Server;
and preserve Host plus `X-Forwarded-Proto=https`.

Before release:

```powershell
Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run test:e2e
```

## 8. Implementation references

- BFF route: [route.ts](../../Frontend/app/api/asterloom/%5B...path%5D/route.ts)
- Auth configuration: [config.ts](../../Frontend/lib/auth/config.ts)
- Sessions: [session.ts](../../Frontend/lib/auth/session.ts)
- Redis store: [store.ts](../../Frontend/lib/auth/store.ts)
- CSRF: [request-security.ts](../../Frontend/lib/auth/request-security.ts)
- Kiota client: [asterloom-client.ts](../../Frontend/lib/api/asterloom-client.ts)
- Theme: [theme.ts](../../Frontend/lib/ui/theme.ts)
- Localization: [locale.ts](../../Frontend/lib/i18n/locale.ts)
- Deployment configuration: [asterloom.conf](../../Deploy/nginx/asterloom.conf)
