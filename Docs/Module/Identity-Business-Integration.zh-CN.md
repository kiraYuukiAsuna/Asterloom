# 业务应用接入统一账号与登录

[简体中文](Identity-Business-Integration.zh-CN.md) | [English](Identity-Business-Integration.md) | [Identity](Identity.zh-CN.md)

本文给出 Asterloom 的标准业务接入链路。桌面、移动或其他可打开系统浏览器的客户端使用 Public Client + Authorization Code + PKCE 登录；客户端取得用户 Access Token 后，直接用它访问业务后端。业务后端本地验证签名和业务 Audience，不需要保存用户 Token，也不需要在每次请求时用自己的 Client Secret 换 Token。

Confidential Client 仍然必要，但它只属于可信业务后端，用于注册账号、确认邮箱、后台任务和管理调用，不能嵌入客户端。

## 1. 账号与应用模型

```text
Global Passport account (one stable sub)
  ├─ Membership: Business A
  │    ├─ Public Client A → end-user PKCE login
  │    ├─ Confidential Client A → registration/service operations
  │    └─ API Scope A → Audience business-a-api
  └─ Membership: Business B
       ├─ Public Client B
       ├─ Confidential Client B
       └─ API Scope B → Audience business-b-api
```

- Passport 账号全局唯一；用户在 A、B 登录得到相同的 `sub`。
- Membership、Token 的 `application_id` 和权限作用域按业务 Application 隔离。
- 管理员和业务用户使用同一套账号表；管理员只是拥有受信任的 Passport Role。
- Asterloom Web 是管理控制面。业务自己提供最终用户 UI；Public Client 登录时会跳到 Passport 完成认证，再回到业务客户端。

## 2. 一个业务应创建哪些资源

在 Web 管理后台创建或选择 Tenant/Application 后，创建以下三项：

1. API Scope，例如 `business-a.api`，Resources 填业务后端唯一 Audience，例如 `business-a-api`。
2. Public Native Client：启用 `authorization_code`、`refresh_token` 和 PKCE，绑定当前 Tenant/Application，授权 `openid profile email roles offline_access asterloom.api business-a.api`，填写精确 Redirect URI；Public Client 没有 Client Secret。
3. Confidential Web Client：绑定同一 Tenant/Application，启用 `client_credentials`；如果业务后端要调用账号注册 API，则启用 `allow_user_registration`。浏览器 BFF 如需登录用户，应另建启用 `authorization_code`、`refresh_token` 的 Web Client。

`allow_membership_auto_join` 决定已存在的全局账号首次通过该 Client 登录时是否自动加入应用。A、B 不得共用 Client ID、Client Secret 或 API Audience。

## 3. Public Client 登录并调用业务 API

桌面客户端使用 `Asterloom.Sdk.Identity`。SDK 打开系统浏览器、启动本机 Loopback Callback，并自动使用 PKCE：

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = "business-a-desktop";
    options.EnableInteractiveAuthentication = true;
    options.RequestRefreshTokens = true;
    options.Scopes.Add("business-a.api");
});

var identity = services.GetRequiredService<AsterloomIdentityClient>();
var tokens = await identity.SignInAsync(cancellationToken: cancellationToken);

using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.business-a.example/me");
request.Headers.Authorization =
    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
using var response = await httpClient.SendAsync(request, cancellationToken);
response.EnsureSuccessStatusCode();
```

只把 Access Token 发给业务 API。ID Token 只描述登录结果，Refresh Token 只交给 Passport 换新 Token，两者都不能当 API Bearer Token。

原生客户端应把 Token 放在操作系统安全存储中，并按 SDK 的刷新流程续期；不要写入配置文件、命令行、日志、Telemetry 或 Analytics。Public Client 本身无法保密，因此绝不能配置 Client Secret。

## 4. ASP.NET Core 业务后端验证 Token

业务后端引用 `Asterloom.Sdk.Identity.AspNetCore`：

```csharp
builder.Services.AddAsterloomResourceServer(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.Audience = "business-a-api";
    options.TenantId = Guid.Parse(configuration["Asterloom:TenantId"]!);
    options.ApplicationId = Guid.Parse(configuration["Asterloom:ApplicationId"]!);
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/me", (ClaimsPrincipal user) => new
{
    subject = user.FindFirstValue("sub"),
    applicationId = user.FindFirstValue("application_id"),
}).RequireAuthorization();
```

SDK 通过 Issuer 的 OIDC Discovery/JWKS 本地验证：

- RS256 签名、`typ=at+jwt`、Issuer、Audience、过期时间；
- `sub` 存在且 `asterloom_actor_type=user`；
- 配置后，`tenant_id`、`application_id` 必须精确匹配。

因此普通 API 请求不需要业务 Client Secret，也不需要回调 Asterloom。签名密钥会按标准 Metadata 刷新。生产必须使用 HTTPS；明文 HTTP 仅能通过 `AllowInsecureHttpForDevelopment` 显式用于 Loopback 开发环境。

### 实时 Asterloom 权限判断

需要 Asterloom Role/Policy 的端点可增加远程 Permission Policy：

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("platform-read", policy => policy
        .RequireAuthenticatedUser()
        .RequireAsterloomPermission("platform.info.read"));

app.MapGet("/platform", Handle)
    .RequireAuthorization("platform-read");
```

该 Handler 会把当前请求的同一份用户 Access Token 传给 Asterloom `CheckPermission`。Asterloom 从 Token 读取 `sub`，锁定 Tenant/Application，实时检查 Membership 和 Role/Policy；业务后端仍不保存 Token。权限服务不可达或拒绝时按 Fail Closed 处理。

该便捷 Policy 适合 RBAC 和 ACL 自检，但 User Token 不能提交自定义 ABAC 属性。需要基于订单金额、部门、所有者或风控
结果授权时，业务 API 先正常验证用户 Access Token，再从自身数据库读取权威属性，最后用业务 Confidential Client 的
Service Token 调用 `AsterloomAuthorizationClient.CheckAccessAsync(actorId: userSub, ...)`。Asterloom 会确认目标 `sub`
仍是该 Application 的活动成员。完整调用链和代码见 [Authorization：RBAC、ACL 与 ABAC](Authorization.zh-CN.md)。

## 5. 可信后端注册账号

注册不是 Public Client 的能力。业务注册表单提交到业务后端，后端用自己的 Confidential Client 获取 Service Token，再调用账号 API：

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = configuration["Asterloom:Identity:ClientId"]!;
    options.ClientSecret = configuration["Asterloom:Identity:ClientSecret"]!;
    options.EnableServiceCredentials = true;
});

var identity = services.GetRequiredService<AsterloomIdentityClient>();
using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    cancellationToken => identity.GetServiceAccessTokenAsync(
        cancellationToken: cancellationToken));
var accounts = new AsterloomIdentityAccessClient(transport.CallInvoker);

var registration = await accounts.RegisterAccountAsync(
    email, displayName, password, cancellationToken);
await accounts.ConfirmEmailAsync(email, confirmationToken, cancellationToken);
```

`AsterloomAuthenticatedTransport` 的 URI 是 Asterloom API 地址，不是重复填写 Client 身份。Issuer 负责 OIDC 协议和 Token；Transport 地址负责实际发送 gRPC/HTTP API。二者同域部署时值通常相同。

注册 API 只能操作 Confidential Client 绑定的 Application：新邮箱创建全局账号；已有邮箱且密码正确时复用同一 `sub` 并添加/恢复当前 Membership；错误密码或异常账号统一拒绝。

## 6. Web/BFF 与用户 Token 保存

浏览器业务推荐 Authorization Code + PKCE，由业务 BFF 完成 OIDC Callback，并把用户 Token 加密存入服务端 Session Store；浏览器只拿随机 `HttpOnly`、`Secure` Session Cookie。此时是 BFF 为每个浏览器 Session 保存用户 Token并负责 Refresh。

原生 Public Client 场景不同：Token 由客户端安全存储和刷新，业务后端只验证每次请求携带的 Access Token，不保存它。Confidential Client 的 Service Token 又是第三类独立身份，不能替代用户 Token访问需要用户身份的 API。

Password Grant 已禁用。无论原生客户端还是浏览器 BFF，都不得收集 Passport 密码向 Token Endpoint 直接换取 Token。

## 7. 失效边界

- Access Token 默认 10 分钟。仅使用本地 `.RequireAuthorization()` 的业务 API 在 Token 过期前不会实时得知 Membership、账号状态或权限变化。
- Refresh 时 Passport 会重新检查账号与 Membership；移除 Membership 后 Refresh 立即失败。
- 使用 `RequireAsterloomPermission` 的请求每次都会远程检查 Membership 和权限，因此可立即生效。
- 高风险端点应使用远程 Permission Policy；普通低风险端点可接受短 Access Token 的失效窗口。
- A、B 使用不同 Audience/Application，A 的 Token 不能访问 B 的资源服务器。

## 8. 可运行参考与测试

`Provision-Reference-App.sh` 会创建：

- `asterloom.reference.api` → `asterloom-reference-api`；
- 绑定到参考 Application 的 `asterloom-reference-native` Public Client；
- 注册/服务使用的 Confidential Client；
- Reference Backend 的 Issuer/Audience/Tenant/Application 配置。

运行 `Asterloom.ReferenceApp.Client login` 后，示例会完成 PKCE 登录，并用所得 Access Token 调用 `/api/reference/me`。相关实现：

- [ReferenceProtectedEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceProtectedEndpoints.cs)
- [ReferenceBusinessAuthorizationEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceBusinessAuthorizationEndpoints.cs)
- [Reference Resource Server 配置](../../Backend/Samples/Asterloom.ReferenceApp.Backend/Program.cs)
- [Reference Public Client](../../Backend/Samples/Asterloom.ReferenceApp.Client/Program.cs)
- [ASP.NET Core Resource Server SDK](../../Backend/Asterloom.Sdk.Identity.AspNetCore/AsterloomResourceServerServiceCollectionExtensions.cs)

集成测试还会验证 Access Token 是公开可验证的三段式 `at+jwt`、JWKS 包含对应签名密钥、资源服务器接受 Access Token、
拒绝 ID Token，并覆盖 Application Permission、RBAC、ACL、ABAC、归档失效与后端代表用户检查。

## 9. 相关契约

- [identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- [identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- [authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- [Authorization 文档](Authorization.zh-CN.md)
