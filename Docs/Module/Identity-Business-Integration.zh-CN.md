# 业务应用接入统一账号与登录

[简体中文](Identity-Business-Integration.zh-CN.md) | [English](Identity-Business-Integration.md) | [Identity](Identity.zh-CN.md)

本文说明业务 A、业务 B 等产品如何把 Asterloom 当作统一 Passport 使用。业务应用保留自己的注册、登录和找回密码页面；密码、Client Secret 和 OIDC Token 只在业务后端与 Asterloom 之间传递。

## 1. 账号模型

```text
Global Passport account (one user ID / sub)
  ├─ Application membership: Business A (active/removed)
  │    └─ Authorization bindings and policies for Business A
  └─ Application membership: Business B (active/removed)
       └─ Authorization bindings and policies for Business B
```

- Passport 账号是全局唯一的，同一邮箱只创建一个账号和一个稳定的 `sub`。
- 应用成员关系独立。用户可以加入 A、不加入 B，也可以只从 A 移除。
- Authorization 的 Role Binding/Policy 按 Tenant/Application/Environment Scope 隔离。
- 管理后台账号与业务用户不是两套表。它们都是 Passport 账号；区别在于管理人员拥有受信任的 Passport Role，普通业务用户的 `roles` 通常为空。
- Asterloom Web 是管理控制面，不承载各业务面向最终用户的注册或登录页面。

用户先在业务 A 注册后，业务 B 可以复用同一账号。B 可选择：

- 开启 `allow_membership_auto_join`，用户首次正确登录时自动加入 B；或
- 通过 B 的可信后端再次调用注册 API。用户必须提供同一邮箱和正确密码，Asterloom 只增加 B 的成员关系，不创建第二个账号。

## 2. 每个业务应用需要的资源

在 Web 管理后台依次完成：

1. 在 `/tenants` 创建或选择 Tenant 和 Application。
2. 在 `/identity/users` 的 **OIDC clients** 创建一个 Confidential Web Client。
3. 将 Client 绑定到准确的 `tenantId` 和 `applicationId`。
4. 启用 `client_credentials` 和 `password`；需要长会话时再启用 `refresh_token`。
5. 开启 `allow_user_registration`，允许这个可信后端调用注册接口。
6. 按产品策略决定是否开启 `allow_membership_auto_join`。
7. 立即保存只显示一次的 Client Secret，并从 Secret Manager 注入业务后端。
8. 在 `/authorization/roles` 给用户或服务主体配置应用作用域的 Role Binding/Policy。

一个 OIDC Client 只能绑定一个 Platform Application。不要让多个业务共享同一个 Client Secret；业务 A 和 B 应分别注册 Client。

## 3. 业务注册流程

```text
Browser registration form
  → Business backend
      → client_credentials token
      → IdentityAccess.RegisterAccount
          → create/reuse global account
          → create/reactivate this application's membership
          → return one-time email confirmation token
      → Business email provider
Browser confirmation page
  → Business backend
      → IdentityAccess.ConfirmEmail
```

Headless JSON Transcoding API：

| 操作 | HTTP | 作用域来源 |
| --- | --- | --- |
| 注册或加入当前应用 | `POST /api/v1/identity/accounts:register` | 调用方 Client 的应用绑定 |
| 确认邮箱 | `POST /api/v1/identity/accounts:confirm-email` | 调用方 Client 的应用绑定 |
| 读取当前应用账号 | `GET /api/v1/identity/accounts/{userId}` | 调用方 Client 的应用绑定 |
| 移除当前应用成员关系 | `DELETE /api/v1/identity/accounts/{userId}/membership?expectedVersion=...` | 调用方 Client 的应用绑定 |

请求体不能指定 Tenant/Application，因此业务后端无法越权操作其他应用。注册返回的确认 Token 只交给可信后端，由业务自己的邮件模板和确认页面发送；不要把它写入日志或 Analytics。

C# 接入：

```csharp
builder.Services.AddAsterloomIdentityClient(options =>
{
    options.Issuer = new Uri("https://asterloom.example/");
    options.ClientId = configuration["Asterloom:Identity:ClientId"]!;
    options.ClientSecret = configuration["Asterloom:Identity:ClientSecret"]!;
    options.EnableServiceCredentials = true;
    options.EnablePasswordAuthentication = true;
    options.RequestRefreshTokens = true;
});

var identity = services.GetRequiredService<AsterloomIdentityClient>();
using var transport = AsterloomAuthenticatedTransport.Create(
    new Uri("https://asterloom.example/"),
    cancellationToken => identity.GetServiceAccessTokenAsync(
        cancellationToken: cancellationToken));
var accounts = new AsterloomIdentityAccessClient(transport.CallInvoker);

var registration = await accounts.RegisterAccountAsync(
    email,
    displayName,
    password,
    cancellationToken);

// 由业务邮件服务发送 registration.EmailVerificationToken。
await accounts.ConfirmEmailAsync(email, confirmationToken, cancellationToken);
```

`RegisterAccountAsync` 的重要语义：

- 邮箱不存在：创建 Pending 全局账号和当前应用成员关系。
- 邮箱已存在且密码正确：复用原账号，只创建或恢复当前应用成员关系。
- 邮箱已存在但密码不正确，或账号已 Suspended/Archived：统一拒绝，避免直接接管已有账号。
- 邮箱确认后，Pending 账号转为 Active；已确认的全局账号加入新应用时无需重复确认。

## 4. 业务登录与 BFF Session

浏览器只把账号密码提交给自己的同源业务后端。业务后端使用其 Confidential Client 完成受控 Password Grant：

```csharp
var tokens = await identity.AuthenticateWithPasswordAsync(
    email,
    password,
    cancellationToken);
```

成功 Token 包含稳定的全局 `sub`，并包含 Client 绑定的 `tenant_id`、`application_id` 和 `asterloom_actor_type=user`。A、B 登录得到相同 `sub`，但 `application_id` 不同。

业务后端应：

1. 把 Access/Refresh Token 加密保存在服务端 Session Store（生产建议 Redis 或数据库）。
2. 只给浏览器设置随机、不透明、`HttpOnly`、`Secure`、合适 `SameSite` 的 Session Cookie。
3. 每个浏览器 Session 独立保存一份 Token；不要把用户 Token 放进单例 `IAsterloomTokenStore`。
4. 接近过期时调用 `RefreshUserTokensAsync(currentTokens)`，并原子替换服务端 Session。
5. Refresh 或 API 返回未认证/拒绝时清理 Session，让用户重新登录。

禁止浏览器直接使用 Password Grant；禁止把 Client Secret、Access Token 或 Refresh Token放入 JavaScript、Local Storage、Cookie 明文、日志、Telemetry 或 Analytics。

## 5. 成员关系和权限失效

成员关系与权限是两层独立条件：

```text
active application membership
  AND Authorization decision allows permission
  → request allowed
```

- 移除应用成员关系后，该应用绑定 Token 的受保护 API 调用立即拒绝，Refresh 也失败。
- 如果该 Client 开启 `allow_membership_auto_join`，用户下次以正确密码登录会重新激活成员关系；需要管理员移除持续生效时必须关闭自动加入，或使用全局 Suspend / Authorization Deny。
- 其他应用的成员关系、登录和权限不受影响。
- Suspended/Archived 是全局账号状态，会阻止所有应用继续登录或 Refresh。
- 移除 Role Binding/添加 Deny Policy 只改变权限，不删除成员关系。
- 恢复成员关系不会自动恢复以前删除的 Role Binding。

## 6. 管理后台职责

`/identity/users` 覆盖管理面 API：

- 全局账号创建、邀请、资料、Passport Role、密码重置、暂停、归档与恢复。
- OIDC Session 查看和撤销。
- Application Membership 查询、添加/恢复和移除。
- OIDC Client 的应用绑定、Grant、注册/自动加入开关、Redirect URI、Scope、Secret 轮换与删除。
- OIDC Scope 管理。

业务 Headless API 不在管理后台中模拟调用，因为它必须以具体业务 Client 的 Secret 标识应用。管理后台提供等价的账号和成员关系人工操作，不保存各业务 Client Secret。

## 7. 可运行参考

参考后台实现了完整 BFF 边界：

- [ReferenceIdentityEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityEndpoints.cs)
- [ReferenceIdentityGateway.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityGateway.cs)
- [ReferenceIdentitySessionStore.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentitySessionStore.cs)

配置并启动参考后台后可执行：

```powershell
$env:ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD = "Use-A-Strong-Test-Password!2026"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- \
  account-demo user@example.com "Example User"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- \
  account-login user@example.com
```

`account-demo` 会真实执行注册、邮箱确认、登录、读取服务端 Session 和退出。示例的内存 Session Store 只用于演示，生产必须换成共享、加密、可过期和可撤销的持久 Session Store。
没有配置邮件服务的本地演练可显式启用 `ASTERLOOM_REFERENCE_EXPOSE_CONFIRMATION_TOKEN=true`；该开关会把确认
Token 返回给测试客户端，默认关闭且禁止用于公开生产入口。

## 8. 相关契约

- 业务接入协议：[identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- 管理协议：[identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- C# 业务账号 SDK：[AsterloomIdentityAccessClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityAccessClient.cs)
- 登录 SDK：[AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- 权限接入：[Authorization.zh-CN.md](Authorization.zh-CN.md)
