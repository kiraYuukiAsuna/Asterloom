# Identity：Passport、用户与 OIDC Client

[简体中文](Identity.zh-CN.md) | [English](Identity.md) | [模块索引](README.zh-CN.md)

Identity 负责“你是谁”：用户账号、邀请、登录会话、OIDC/OAuth 2.0 Client、Scope、Token 获取与退出。
具体“你能做什么”由 [Authorization](Authorization.zh-CN.md) 决定。

## 1. 支持的登录模式

| 调用者 | Flow | Client 类型 | Secret |
| --- | --- | --- | --- |
| 桌面/原生客户端 | Authorization Code + PKCE | Public / Native | 禁止内置 |
| Web BFF | Authorization Code | Confidential / Web | 只保存在 BFF 服务端 |
| 后台服务、Job、CI | Client Credentials | Confidential | 从 Secret Manager 注入 |

标准端点：

- `/connect/authorize`
- `/connect/token`
- `/connect/logout`
- `/.well-known/openid-configuration`
- `/passport/invitation`

生产 Issuer 必须固定并使用 HTTPS。Token 的 `iss` 必须与配置完全一致。

## 2. Web 管理

路由：`/identity/users`

同一工作区覆盖三组资源：

### Users

- List/Get 用户，搜索并包含 Archived 状态。
- Invite/Resend Invitation；邀请链接有有效期。
- 修改 Display Name 和 Passport Role。
- Suspend/Reactivate、Archive/Restore。
- 查看 Session，撤销一个 Session 或全部 Session。

### Clients

- List/Get/Create/Update/Delete OIDC Client。
- 配置 Application Type、Client Type、Grant Type、Redirect URI、Post Logout Redirect URI 和 Scope。
- Confidential Client 创建和 Rotate Secret 时只返回一次明文 Secret，必须立即安全保存。

### Scopes

- List/Get/Create/Update/Delete Scope。
- Scope 控制 Token 请求的授权范围；API 权限仍由 Authorization 模块判断。

Passport Role 是平台级账号角色，不等同于可自定义的 Authorization Role。业务权限优先通过
Authorization Role、Binding 和 Policy 管理。

## 3. 服务账号接入

先在 Web 创建 Confidential Client，允许 Client Credentials，并授予 `asterloom.api` Scope：

```csharp
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Rpc;

builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = configuration["Asterloom:ClientId"]!;
    options.ClientSecret = configuration["Asterloom:ClientSecret"]!;
    options.EnableServiceCredentials = true;
});

var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
await identity.GetServiceAccessTokenAsync(cancellationToken: cancellationToken);

using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    identity.GetAccessTokenAsync);
```

Client Secret 不能提交到仓库、镜像或 `appsettings.json`，应由 Secret Manager/环境注入。

## 4. 桌面交互登录

在 Web 创建 Public Native Client，启用 Authorization Code，登记 Loopback Redirect URI：

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = "my-desktop-client";
    options.EnableInteractiveAuthentication = true;
    options.RequestRefreshTokens = true;
    options.RedirectUri = new Uri("http://localhost/");
});

var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
var tokens = await identity.SignInAsync(cancellationToken: cancellationToken);
var accessToken = await identity.GetAccessTokenAsync(cancellationToken);
await identity.SignOutAsync(cancellationToken);
```

SDK 使用系统浏览器和 PKCE。Public Client 不得设置 Client Secret。默认
`AsterloomInMemoryTokenStore` 只适合示例；生产桌面应用应实现 `IAsterloomTokenStore`，用 Windows
DPAPI、macOS Keychain 等操作系统保护存储 Refresh Token。

## 5. Token 生命周期

- `GetAccessTokenAsync` 会复用仍有效的 Token，并在接近过期时 Refresh 或重新获取服务 Token。
- Interactive Client 请求 Refresh Token 时需要 `offline_access`。
- `SignOutAsync` 调用 End Session 并清理本地 Token。
- `ClearLocalSessionAsync` 只清本地状态，不撤销服务端其他 Session。
- 管理员撤销 Session 后，客户端应在下次 API/Refresh 失败时回到登录页。

## 6. 权限

- 用户：`identity.user.read/invite/update/roles.set/suspend/reactivate/archive/restore`
- Session：`identity.session.read/revoke`
- Client：`identity.client.read/create/update/secret.rotate/delete`
- Scope：`identity.scope.read/create/update/delete`

创建 Client/Scope 只是注册认证协议资源；仍需在 Platform 和 Authorization 中配置 Membership、Role、
Binding 或 Policy，Token 才能访问具体 API。

## 7. 安全检查

- Redirect URI 必须精确登记，不能使用开放通配或不受控跳转。
- 非 Loopback HTTP Issuer/Redirect 被 SDK 拒绝；生产只使用 HTTPS。
- Native Client 永不携带 Secret；服务 Secret 必须轮换并可撤销。
- Web 浏览器只持有 HttpOnly Session ID，OIDC Token 留在 BFF。
- 不把 Access/Refresh Token 写入日志、Analytics、Telemetry 或前端 Local Storage。
- 邀请链接和一次性 Secret 按敏感凭据处理。

## 8. 相关实现

- Admin Protocol：[identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- Types：[identity_types.proto](../../Proto/Asterloom/identity/v1/identity_types.proto)
- C# Client：[AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- SDK 注册：[AsterloomIdentityServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityServiceCollectionExtensions.cs)
- 服务端模块：[IdentityModule.cs](../../Backend/Asterloom.Module.Identity/IdentityModule.cs)
- Web：[identity-workspace.tsx](../../Frontend/features/identity/identity-workspace.tsx)
- BFF 说明：[Web-Console-Bff.zh-CN.md](Web-Console-Bff.zh-CN.md)
