# Web Console / BFF：管理后台与浏览器安全边界

[简体中文](Web-Console-Bff.zh-CN.md) | [English](Web-Console-Bff.md) | [模块索引](README.zh-CN.md)

Web Console 是覆盖全部 Asterloom Admin API 的浏览器管理后台。它同时包含 Next.js BFF
（Backend for Frontend）：浏览器不直接持有 OIDC Token，而由同源的 Next.js 服务端管理登录会话并把
请求安全地转发给 Asterloom Server。

## 1. 请求链路

```text
Browser
  ├─ /api/auth/*                 -> Next.js OIDC Login/Callback/Logout
  └─ /api/asterloom/api/v1/*     -> Next.js BFF
                                        ├─ 读取 HttpOnly Session ID
                                        ├─ 从 Redis 读取并解密 Token Session
                                        ├─ 校验 Origin + X-CSRF-Token
                                        ├─ 刷新即将过期的 Access Token
                                        └─ Authorization: Bearer ...
                                              -> Asterloom JSON Transcoding /api/v1/*
```

Next.js BFF 不是新的业务后端。业务规则只实现一次，仍由 Protobuf、gRPC Service 和领域模块负责；BFF
只承担浏览器认证、会话、CSRF、Header 收敛、Token 刷新和同源代理。

原生 C# 应用直接使用 gRPC/C# SDK，不必经过 BFF。外部受信任服务也可直接调用 HTTPS `/api/v1/*`；
只有浏览器管理 UI 使用 `/api/asterloom/*`。

## 2. 登录与会话

Web 使用 OIDC Authorization Code + PKCE：

1. `/api/auth/login` 生成 `state`、`nonce`、Code Verifier/Challenge，并保存 10 分钟 Login Transaction。
2. 浏览器跳转 Passport `/connect/authorize`。
3. `/api/auth/callback` 校验 State/Nonce，用 Code 换 Token，并根据 Passport 登录选择建立 BFF Session。
4. 未勾选“在此设备保持登录”时，浏览器获得无 `Max-Age` 的会话 Cookie，关闭浏览器后失效，服务端 Session
   最长保留 8 小时；勾选时，Cookie 与服务端 Session 都保持 30 天。
5. 浏览器只获得随机的 HttpOnly、SameSite=Lax、Secure（HTTPS 时）Session Cookie。
6. Access Token 临近过期 30 秒时，BFF 使用 Refresh Token 刷新；并发刷新由分布式锁串行化。
7. Logout 删除服务端 Session，清 Cookie，再跳转 Passport End Session；无论是否勾选都会立即退出。

Access Token、Refresh Token 与 ID Token 永远不写入 Local Storage，也不下发给浏览器 JavaScript。
“保持登录”选择由 Passport 作为受签名的 ID Token Claim 传给 BFF，浏览器不能自行把普通会话升级为持久会话。
持久会话对应的滚动 Refresh Token 生命周期也为 30 天；普通登录仍使用默认短期 Token 策略。

## 3. Redis 为什么存在

Redis 只服务于 Web BFF 会话，不保存 Asterloom 领域数据：

- 多个 Next.js 实例共享 Session，滚动重启后登录仍可继续。
- Login Transaction 与 Session 有 TTL，不依赖单进程内存。
- Refresh Lock 防止多个请求同时复用/轮换 Refresh Token。
- Cookie 内只有随机 ID；Redis Key 使用 ID 的 SHA-256 摘要。
- Redis 中的 Session Payload 还使用独立 256-bit AES-GCM Key 加密。

Web 使用 npm 包 `redis`（Node.js），不是 C# 库。当前 Asterloom 后端 C# 不读取 Redis，也没有
StackExchange.Redis 依赖；C# 领域数据通过 Npgsql 访问 PostgreSQL。以后若确需让 C# 使用 Redis，应另行
设计用途和契约，通常选择 `StackExchange.Redis`，不能让 Server 或管理 UI 绕过公开 API 读取 BFF Session。

`memory` Store 仅用于开发/测试，进程退出即丢失且不能多实例共享；生产配置会拒绝使用它。详细决策见
[ADR 0001](../ADR/0001-redis-for-web-bff-sessions.md)。

## 4. CSRF、代理与错误

所有非 GET/HEAD/OPTIONS 请求必须同时满足：

- `Origin` 与 `ASTERLOOM_WEB_ORIGIN` 完全一致。
- `X-CSRF-Token` 与服务端 Session 中的随机值常量时间匹配。

BFF 只向上游传递 `Accept`、`Content-Type`、`If-Match`、`If-None-Match` 和 `X-Request-ID`，并自行注入
Bearer Token。请求超时为 30 秒；Token 导致的首次 401 会在刷新后重试一次。Server Fetch 失败时返回
502 `BACKEND_UNAVAILABLE`，刷新阶段的 Session/Passport 故障返回 503 `SESSION_SERVICE_UNAVAILABLE`，
缺失 Session 返回 401，CSRF 失败返回 403。登录 Callback 的 Passport 故障会重定向到带错误码的登录页。
若 Redis 在初次读取 Session 时直接抛出连接异常，当前 Route 可能由 Next.js 返回通用 500；此时应查看
Web 服务端 Redis 日志，而不是把它误判成领域 API 错误。

页面出现 `An unexpected error occurred.` 时不要只刷新页面：

1. 在浏览器 Network 找失败的 `/api/asterloom/*` 请求，记录 Status、结构化 Error Code 与 `X-Request-ID`。
2. 401 检查 Session/Issuer；403 检查 Permission、Origin 和 CSRF；409 重新读取资源版本；502 检查 BFF 到
   Server 的网络；503 检查 Redis/Passport。
3. 使用 Request ID 在 Server Log、[Audit](Audit.zh-CN.md) 与 [Telemetry](Telemetry.zh-CN.md) 中关联。

## 5. 管理页面覆盖

Web 当前提供以下工作区：

| 能力 | 路由 |
| --- | --- |
| Platform | `/tenants` |
| Identity | `/identity/users` |
| Authorization | `/authorization/roles` |
| Targeting | `/targeting/segments` |
| Feature | `/features` |
| Config | `/config` |
| Release | `/channels`、`/artifacts`、`/releases` |
| Analytics | `/analytics/schemas`、`/analytics/explorer` |
| Telemetry | `/telemetry/sources`、`/telemetry/health` |
| Storage | `/storage/buckets`、`/storage/objects` |
| Audit | `/audit` |
| Operations | `/operations/apis`、`/operations/health` |

`Backend/Tools/Asterloom.ApiCoverage` 把 Admin RPC 与 Permission、Web Route、`data-ui-action` 以及 E2E
测试清单逐项比对。新增 Admin API 时，页面与测试必须在同一变更中完成，不能只实现后端一半。

## 6. UI、主题与语言

技术栈为 React 19、Next.js 16 App Router、TypeScript、Tailwind CSS 4、shadcn-ui/Radix、Lucide、Geist、
SWR、Zustand、Zod、Kiota、Sonner 和 cmdk。

- 主题支持 `system`、`light`、`dark`，保存在 `asterloom-theme` Local Storage，并在首次渲染前解析以避免闪烁。
- 语言支持 `en` 与 `zh-CN`，优先使用用户选择，再回退浏览器语言；选择写入 Local Storage/Cookie。
- 新页面必须同时检查浅色和深色下的 Card、Border、Input、Table、Badge、Toast、Loading、Empty 和 Error 状态。
- 所有用户可见字符串进入翻译表；时间、数字和文件大小使用当前 Locale 格式化。

## 7. 配置与部署

主要环境变量：

| 变量 | 含义 |
| --- | --- |
| `ASTERLOOM_BACKEND_URL` | BFF 在内部访问 Asterloom Server 的地址 |
| `ASTERLOOM_PASSPORT_PUBLIC_URL` | 浏览器跳转到 Passport 的公开地址 |
| `ASTERLOOM_WEB_ORIGIN` | Web 的精确公开 Origin，也是 CSRF Origin |
| `ASTERLOOM_OIDC_ISSUER` | 必须与 Token `iss` 完全一致 |
| `ASTERLOOM_OIDC_CLIENT_ID/SECRET` | Confidential Web Client 凭据 |
| `ASTERLOOM_SESSION_STORE` | 开发 `memory`，生产必须 `redis` |
| `ASTERLOOM_SESSION_REDIS_URL/PASSWORD` | Redis 内网地址与密码 |
| `ASTERLOOM_SESSION_ENCRYPTION_KEY` | Base64 编码的 32-byte AES Key |

生产中 Web Origin、Passport URL 和 Issuer 必须使用 HTTPS；Secret 和加密 Key 从 Secret Manager 注入。
Nginx 应把 `/` 转发 Web，把 `/connect`、`/.well-known`、`/passport`、`/api/v1` 与 `/health` 转发 Server，
并正确设置 Host 与 `X-Forwarded-Proto=https`。

发布前执行：

```powershell
Set-Location Frontend
npm ci
npm run lint
npm run typecheck
npm test
npm run build
npm run test:e2e
```

## 8. 相关实现

- BFF Route：[route.ts](../../Frontend/app/api/asterloom/%5B...path%5D/route.ts)
- Auth 配置：[config.ts](../../Frontend/lib/auth/config.ts)
- Session：[session.ts](../../Frontend/lib/auth/session.ts)
- Redis Store：[store.ts](../../Frontend/lib/auth/store.ts)
- CSRF：[request-security.ts](../../Frontend/lib/auth/request-security.ts)
- Kiota Client：[asterloom-client.ts](../../Frontend/lib/api/asterloom-client.ts)
- 主题：[theme.ts](../../Frontend/lib/ui/theme.ts)
- 国际化：[locale.ts](../../Frontend/lib/i18n/locale.ts)
- 部署配置：[asterloom.conf](../../Deploy/nginx/asterloom.conf)
