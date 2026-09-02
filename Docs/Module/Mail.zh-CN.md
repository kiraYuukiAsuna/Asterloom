# 邮件发送

[简体中文](Mail.zh-CN.md) | [English](Mail.md)

Mail 能力允许各业务的机密后端把事务邮件内容提交给 Asterloom，由平台集中管理 SMTP 账号并通过 MailKit 发送。管理员只在创建或轮换账号时通过 Web 控制台写入授权码；保存后 API 不会再返回凭据，业务后端也不需要持有发件授权码。

## 作用域与职责

每个 SMTP 账号归属于一个 `Tenant + Application`。业务机密客户端的 Access Token 同样绑定租户与应用，因此即使知道其他应用的 SMTP Account ID，也不能跨应用使用发件账号。

Asterloom Mail 当前提供：

- SMTP 账号的创建、查询、更新、归档、恢复与实际发信测试。
- 强制 STARTTLS 或连接时 SSL/TLS，不提供明文 SMTP。
- SMTP 授权码/密码只写不读，并使用 ASP.NET Core Data Protection 加密。
- 纯文本与 HTML 正文、To/CC/BCC、Reply-To，单封最多 100 个收件地址。
- 由调用方提供 Client Message ID 作为幂等键，避免业务重试时重复发信。
- 同步发送结果与管理端投递历史。

当前不包含附件、平台内邮件模板、营销群发、退信处理和异步重试队列。邮件模板和最终正文由业务后端生成后提交。投递历史保存收件地址、主题、结果、时间和服务商 Message ID，但不会保存正文或 SMTP 凭据。

## 在 Web 控制台配置 QQ 邮箱

1. 在 QQ 邮箱设置中开启 SMTP 服务并生成第三方客户端授权码。
2. 打开 **邮件 → SMTP 账号**，选择目标租户和应用。
3. 创建账号时填写：

   - SMTP 主机：`smtp.qq.com`
   - 端口：`465`
   - 传输安全：**连接时使用 SSL/TLS**
   - SMTP 用户名：完整 QQ 邮箱地址
   - 授权码/密码：QQ 邮箱生成的 SMTP 授权码，不能填写 QQ 登录密码
   - 发件地址：通常与 QQ 邮箱地址相同

4. 选中账号并发送测试邮件。状态为 `SENT` 表示 SMTP 鉴权成功且邮件服务器接受了消息；失败时会返回 `smtp_authentication_failed`、`smtp_tls_failed` 等稳定错误码。

列表和详情 API 永远不会返回授权码。更新账号时授权码留空表示继续使用原有加密凭据。

## 业务后端接入

业务后端使用自己的 Confidential Client 获取服务 Access Token，构造自动添加 Bearer Token 的 `HttpClient`，然后使用 `Asterloom.Sdk.Mail`：

```csharp
var mail = new AsterloomMailClient(
    authenticatedHttpClient,
    new AsterloomMailScope(tenantId, applicationId));

var delivery = await mail.SendAsync(
    new AsterloomMailMessage(
        smtpAccountId,
        ClientMessageId: $"verify-email:{userId}",
        To: [userEmail],
        Subject: "确认邮箱",
        TextBody: $"你的邮箱验证码是：{code}",
        HtmlBody: $"<p>你的邮箱验证码是：<strong>{encodedCode}</strong></p>"),
    cancellationToken);
```

`authenticatedHttpClient` 必须为每次 Asterloom 请求添加 `Authorization: Bearer <service access token>`。Client Secret 和服务 Token 只能留在业务后端，禁止进入浏览器 JavaScript。

Reference App 已包含完整示例：

- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceServiceTokenHandler.cs`：自动获取并附加服务 Token。
- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceMailGateway.cs`：封装 `AsterloomMailClient`。
- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityEndpoints.cs`：注册成功后提交验证邮件内容。

启用示例：

```json
{
  "Asterloom": {
    "Mail": {
      "Enabled": true,
      "SmtpAccountId": "SMTP_ACCOUNT_UUID",
      "TenantId": "TENANT_UUID",
      "ApplicationId": "APPLICATION_UUID"
    }
  }
}
```

该 SMTP 账号必须属于配置的租户和应用，业务 Confidential Client 还必须在同一作用域拥有 `mail.delivery.send` 权限。

## JSON Transcoding API

业务后端向以下地址提交内容：

```http
POST /api/v1/tenants/{tenantId}/applications/{applicationId}/mail:send
Authorization: Bearer <service-token>
Content-Type: application/json
```

```json
{
  "smtpAccountId": "00000000-0000-0000-0000-000000000000",
  "clientMessageId": "order:20260903-1234",
  "to": ["customer@example.com"],
  "cc": [],
  "bcc": [],
  "replyTo": "support@example.com",
  "subject": "订单已确认",
  "textBody": "你的订单已经确认。",
  "htmlBody": "<p>你的订单已经<strong>确认</strong>。</p>"
}
```

同一租户和应用内再次提交相同 `clientMessageId` 时，会返回原投递记录而不会再次提交 SMTP。该 ID 应来自要通知的业务事件；HTTP 重试时必须复用原 ID，不能每次重新生成。

## 投递状态与错误处理

| 状态 | 含义 |
| --- | --- |
| `PENDING` | 已完成幂等占位，但 SMTP 投递尚未结束。 |
| `SENT` | SMTP 服务器已经接受消息，不代表一定进入收件箱。 |
| `FAILED` | SMTP 鉴权、TLS、网络连接、协议或命令处理失败。 |

C# SDK 收到 `FAILED` 时会抛出 `AsterloomMailDeliveryException`，完整投递记录可从异常的 `Delivery` 属性读取。业务只能把 `SENT` 理解为 SMTP 接受，不能理解为用户已经收到或阅读。

## 权限与安全

| Permission | 用途 |
| --- | --- |
| `mail.account.read` | 查看不含凭据的 SMTP 账号。 |
| `mail.account.create` | 新增 SMTP 账号和授权码。 |
| `mail.account.update` | 更新 SMTP 设置或更换授权码。 |
| `mail.account.archive` / `mail.account.restore` | 控制账号是否允许发信。 |
| `mail.account.test` | 从控制台发送测试邮件。 |
| `mail.delivery.read` | 查看投递元数据。 |
| `mail.delivery.send` | 提交应用邮件。 |

生产环境必须持久化 `Identity:DataProtectionKeysPath`。如果密钥环丢失或被替换，已有 SMTP 凭据将无法解密，需要在控制台重新填写授权码。Mail API 的请求正文可能包含个人信息或安全验证码，禁止把请求正文写入日志。
