# Identity：全局 Passport 账号与应用接入

[简体中文](Identity.zh-CN.md) | [English](Identity.md) | [模块索引](README.zh-CN.md)

Identity 负责“你是谁”：全局用户账号、应用成员关系、邀请、登录会话、OIDC/OAuth 2.0 Client、Scope、
Token 获取与退出。具体“你能做什么”由 [Authorization](Authorization.zh-CN.md) 决定。管理人员与普通业务
用户使用同一套 Passport 账号；管理人员通过受信任 Passport Role 区分，业务权限则由成员关系和 Authorization 隔离。

## 1. 支持的登录模式

| 调用者 | Flow | Client 类型 | Secret |
| --- | --- | --- | --- |
| 桌面/原生客户端 | Authorization Code + PKCE | Public / Native | 禁止内置 |
| 管理 Web BFF | Authorization Code | Confidential / Web | 只保存在 BFF 服务端 |
| 代用户注册/登录的业务后端 | Client Credentials + 受控 Password Grant | Confidential / Web，绑定应用 | 只保存在业务后端 |
| 后台服务、Job、CI | Client Credentials | Confidential | 从 Secret Manager 注入 |

标准端点：

- `/connect/authorize`
- `/connect/token`
- `/connect/userinfo`
- `/connect/logout`
- `/.well-known/openid-configuration`
- `/passport/invitation`

可信业务后端 API 同时提供原生 gRPC 与 `/api/v1/identity/accounts*` JSON Transcoding 路由，完整流程见
[业务应用统一账号接入](Identity-Business-Integration.zh-CN.md)。

生产 Issuer 必须固定并使用 HTTPS。Token 的 `iss` 必须与配置完全一致。

## 2. Web 管理

路由：`/identity/users`

同一工作区覆盖四组资源：

### Users

- List/Get 全局用户，搜索并包含 Archived 状态。
- 直接创建账号，或通过有时效的激活链接邀请运维人员。
- Invite/Resend Invitation；邀请链接有有效期。
- 修改 Display Name 和 Passport Role。
- 重置密码并撤销相关 Session。
- Suspend/Reactivate、Archive/Restore。
- 查看 Session，撤销一个 Session 或全部 Session。

### Application Memberships

- 按用户、Tenant、Application 查询成员关系，并可包含已移除记录。
- 将已有全局账号加入或恢复到某个 Platform Application。
- 只移除一个应用的访问权，不删除全局账号，也不影响其他应用成员关系。

### Clients

- List/Get/Create/Update/Delete OIDC Client。
- 配置 Application Type、Client Type、Grant Type、Redirect URI、Post Logout Redirect URI 和 Scope。
- 可将 Client 绑定到一个 Tenant/Application，并分别控制可信注册与登录自动加入。
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

Client Secret 不能提交到仓库、镜像或 `appsettings.json`，应由 Secret Manager/环境注入。未绑定的服务 Client
代表平台服务；业务 Client 必须绑定到一个 Platform Application。

## 4. 业务应用注册与登录

每个业务保留自己的浏览器页面，只由可信业务后端调用 Asterloom。注册使用
`AsterloomIdentityAccessClient`，登录使用 `AuthenticateWithPasswordAsync`；业务后端把每个用户的 Token
保存在服务端 Session，只向浏览器返回不透明的 HttpOnly Cookie。

同一全局账号在所有业务中保持同一个 `sub`，每个 Token 同时携带绑定的 `tenant_id` 和 `application_id`。
移除某个应用成员关系后，该应用的受保护 API 与 Refresh 立即失败，其他应用不受影响。完整配置和 C# 示例见
[业务应用统一账号接入](Identity-Business-Integration.zh-CN.md)。

## 5. 桌面交互登录

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

## 6. Token 生命周期

- `GetAccessTokenAsync` 会复用仍有效的 Token，并在接近过期时 Refresh 或重新获取服务 Token。
- Interactive Client 请求 Refresh Token 时需要 `offline_access`。
- `SignOutAsync` 调用 End Session 并清理本地 Token。
- `ClearLocalSessionAsync` 只清本地状态，不撤销服务端其他 Session。
- 管理员撤销 Session 后，客户端应在下次 API/Refresh 失败时回到登录页。
- `RefreshUserTokensAsync(currentTokens)` 用于刷新某个 BFF 用户的 Token，不写入 SDK 共享 Token Store。
- 应用绑定的用户 Token 在 API 权限判断和 Refresh 时都会检查 Active Membership。

## 7. 权限

- 用户：`identity.user.read/create/invite/update/roles.set/password.reset/suspend/reactivate/archive/restore`
- Session：`identity.session.read/revoke`
- 应用成员关系：`identity.application-membership.read/set/remove`
- Client：`identity.client.read/create/update/secret.rotate/delete`
- Scope：`identity.scope.read/create/update/delete`

创建 Client/Scope 只是注册认证协议资源；仍需在 Platform 和 Authorization 中配置 Membership、Role、
Binding 或 Policy，Token 才能访问具体 API。

## 8. 安全检查

- Redirect URI 必须精确登记，不能使用开放通配或不受控跳转。
- 非 Loopback HTTP Issuer/Redirect 被 SDK 拒绝；生产只使用 HTTPS。
- Native Client 永不携带 Secret；服务 Secret 必须轮换并可撤销。
- Web 浏览器只持有 HttpOnly Session ID，OIDC Token 留在 BFF。
- Password Grant 仅允许绑定应用的 Confidential 可信后端使用，浏览器 JavaScript 禁止直接调用。
- 每个业务应用使用独立 Client 和 Secret。
- 不把 Access/Refresh Token 写入日志、Analytics、Telemetry 或前端 Local Storage。
- 邀请链接和一次性 Secret 按敏感凭据处理。

## 9. 相关实现

- Admin Protocol：[identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- 业务接入 Protocol：[identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- Types：[identity_types.proto](../../Proto/Asterloom/identity/v1/identity_types.proto)
- C# Client：[AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- 业务账号 Client：[AsterloomIdentityAccessClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityAccessClient.cs)
- SDK 注册：[AsterloomIdentityServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityServiceCollectionExtensions.cs)
- 服务端模块：[IdentityModule.cs](../../Backend/Asterloom.Module.Identity/IdentityModule.cs)
- Web：[identity-workspace.tsx](../../Frontend/features/identity/identity-workspace.tsx)
- BFF 说明：[Web-Console-Bff.zh-CN.md](Web-Console-Bff.zh-CN.md)
- 业务接入说明：[Identity-Business-Integration.zh-CN.md](Identity-Business-Integration.zh-CN.md)
