# Asterloom 标准协议端点

本文记录不包装为自定义业务 gRPC Service 的标准端点。它们不计入 Admin API/UI
覆盖率，但必须保留协议、集成与安全测试。

## Passport / OAuth 2.0 / OpenID Connect

| 路径 | 方法 | 用途 |
| --- | --- | --- |
| `/.well-known/openid-configuration` | GET | OIDC Discovery |
| `/.well-known/jwks` | GET | JSON Web Key Set |
| `/connect/authorize` | GET、POST | Authorization Code + PKCE 授权 |
| `/connect/token` | POST | Code、Refresh Token、Client Credentials 交换 |
| `/connect/userinfo` | GET、POST | OIDC UserInfo |
| `/connect/logout` | GET、POST | OIDC End Session |
| `/passport/login` | GET、POST | Passport 本地账户交互登录 |

约束：

- 交互式客户端只使用 Authorization Code Flow + S256 PKCE。
- 服务账户使用 Client Credentials；不启用 Implicit 和 Password Flow。
- Refresh Token 默认轮换且重用宽限为零；已兑换 Token 不得再次使用，BFF 必须串行刷新。
- 生产环境必须配置固定 HTTPS Issuer、外部签名/加密证书和持久化 Data
  Protection Key Ring。
- Passport Cookie 为 HttpOnly；生产环境同时要求 Secure 和 `__Host-` 前缀。
- Passport 登录成功后返回不可缓存的同源过渡页，再发起新的顶层授权导航，确保浏览器已提交新会话 Cookie。
- `/passport/login` 与 `/connect/token` 分别采用独立 IP 限流策略。

## 运行状态

| 路径 | 方法 | 用途 |
| --- | --- | --- |
| `/health/live` | GET | Liveness Probe |
| `/health/ready` | GET | Readiness Probe |
| `/health/startup` | GET | Startup Probe |
| `/swagger/v1/swagger.json` | GET | 开发环境 OpenAPI 文档 |

所有自定义业务能力仍必须在 `.proto` 中声明 `google.api.http`，同时通过原生
gRPC、JSON Transcoding、OpenAPI、生成客户端和 Admin API/UI 覆盖检查。
