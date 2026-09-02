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
| 业务后端注册账号 | Client Credentials | Confidential / Web，绑定应用 | 只保存在业务后端 |
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
- Bootstrap 创建的 `Asterloom Web Console` 是管理 BFF 登录使用的系统 Client。API 返回
  `isSystem=true`、`isMutable=false`，并拒绝更新、轮换 Secret 和删除；回调地址或 Secret 必须通过部署
  配置修改，然后重新运行 Migration/Bootstrap Service。

### Scopes

- List/Get/Create/Update/Delete Scope。
- Scope 控制 Token 请求的授权范围；API 权限仍由 Authorization 模块判断。
- Bootstrap 创建的 `asterloom.api` 是系统 Scope，禁止更新和删除。

Web 管理后台会把系统资源显示为只读并隐藏破坏性操作；真正的安全边界仍在后端，不能依赖隐藏按钮。

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

标准原生链路是 Public Client 使用 Authorization Code + PKCE 登录，随后把用户 Access Token 直接作为
Bearer 发送给业务后端；业务后端使用 `Asterloom.Sdk.Identity.AspNetCore` 验证签名、Issuer、业务 Audience
以及 Tenant/Application 绑定。它不保存用户 Token，也不使用自己的 Client Secret 代替用户身份。

账号注册仍由可信业务后端通过 Confidential Client 和 `AsterloomIdentityAccessClient` 完成。浏览器业务采用
OIDC BFF 时，BFF 使用 Authorization Code + S256 PKCE，为每个浏览器 Session 加密保存用户 Token，只返回
不透明 HttpOnly Cookie。业务后端不得接收 Passport 密码来直接换取 Token。

同一全局账号在所有业务中保持同一个 `sub`，不同业务 Token 携带各自的 `application_id` 和 Audience。完整配置、
失效边界和 C# 示例见
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
    options.Scopes.Add("my-business.api");
});

var identity = host.Services.GetRequiredService<AsterloomIdentityClient>();
var tokens = await identity.SignInAsync(cancellationToken: cancellationToken);
var accessToken = await identity.GetAccessTokenAsync(cancellationToken);
// 把 accessToken 作为 Bearer 发送给业务 API；不要发送 ID Token。
await identity.SignOutAsync(cancellationToken);
```

SDK 使用系统浏览器和 PKCE。Public Client 不得设置 Client Secret。默认
`AsterloomInMemoryTokenStore` 只适合示例；生产桌面应用应实现 `IAsterloomTokenStore`，用 Windows
DPAPI、macOS Keychain 等操作系统保护存储 Refresh Token。

## 6. Token 生命周期

- Passport 登录页未勾选“在此设备保持登录”时使用浏览器会话 Cookie，关闭浏览器后失效；勾选时 Passport
  与管理 Web BFF 会话保持 30 天。显式退出始终立即清除两层会话。
- `GetAccessTokenAsync` 会复用仍有效的 Token，并在接近过期时 Refresh 或重新获取服务 Token。
- Interactive Client 请求 Refresh Token 时需要 `offline_access`。
- `SignOutAsync` 调用 End Session 并清理本地 Token。
- `ClearLocalSessionAsync` 只清本地状态，不撤销服务端其他 Session。
- 管理员撤销 Session 后，客户端应在下次 API/Refresh 失败时回到登录页。
- Refresh 会重新检查 Active Membership；只做本地 JWT 验证的外部业务 API 最长存在一个 Access Token 生命周期的失效窗口。
- 使用 Resource Server SDK 的 `RequireAsterloomPermission` 时会远程实时检查 Membership 与 Role/Policy。

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
- Password Grant 与 Implicit Grant 均禁用；用户登录只使用 Authorization Code + S256 PKCE。
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
- ASP.NET Core 资源服务器 SDK：[AsterloomResourceServerServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity.AspNetCore/AsterloomResourceServerServiceCollectionExtensions.cs)
- 服务端模块：[IdentityModule.cs](../../Backend/Asterloom.Module.Identity/IdentityModule.cs)
- Web：[identity-workspace.tsx](../../Frontend/features/identity/identity-workspace.tsx)
- BFF 说明：[Web-Console-Bff.zh-CN.md](Web-Console-Bff.zh-CN.md)
- 业务接入说明：[Identity-Business-Integration.zh-CN.md](Identity-Business-Integration.zh-CN.md)
