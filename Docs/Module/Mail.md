# Mail

[English](Mail.md) | [简体中文](Mail.zh-CN.md)

The Mail capability lets confidential business backends submit transactional email content to Asterloom while SMTP credentials remain centrally managed and encrypted. An administrator enters the authorization code only when creating or rotating an account; APIs never return it afterward, and business backends do not hold it. Asterloom sends through MailKit and records delivery metadata without retaining message bodies.

## Scope and responsibility

An SMTP account belongs to one `Tenant + Application`. A confidential client's access token is bound to the same tenant and application, so it cannot select another application's sender even if it knows the account ID.

Asterloom Mail provides:

- SMTP account create, read, update, archive, restore, and live test operations.
- Mandatory STARTTLS or SSL/TLS-on-connect transport.
- Write-only SMTP authorization codes/passwords protected by ASP.NET Core Data Protection.
- Text and HTML bodies, To/CC/BCC, Reply-To, and up to 100 recipients.
- A caller-supplied client message ID used as an idempotency key.
- Synchronous delivery status and searchable administrative delivery history.

It currently does not provide attachments, stored templates, marketing campaigns, bounce processing, or an asynchronous retry queue. Business code owns the message template and submits the rendered content. Delivery history stores recipients, subject, result, timestamps, and provider message ID, but not either body or the SMTP credential.

## Configure QQ Mail in the Web console

1. In QQ Mail settings, enable SMTP and create a third-party authorization code.
2. Open **Mail → SMTP accounts** and select the target tenant and application.
3. Create an account with:

   - Host: `smtp.qq.com`
   - Port: `465`
   - Security: **SSL/TLS on connect**
   - Username: the complete QQ email address
   - Authorization code/password: the generated QQ SMTP authorization code, not the QQ login password
   - From address: normally the same QQ email address

4. Select the account and send a test message. A `SENT` result confirms SMTP authentication and delivery acceptance. A failed result includes a stable error code such as `smtp_authentication_failed` or `smtp_tls_failed`.

The credential is never returned by list/get APIs. Leave the authorization-code field empty during update to preserve the existing encrypted value.

## Business backend integration

The business backend uses its confidential Identity client to obtain a service token. Create an authenticated `HttpClient`, then use `Asterloom.Sdk.Mail`:

```csharp
var mail = new AsterloomMailClient(
    authenticatedHttpClient,
    new AsterloomMailScope(tenantId, applicationId));

var delivery = await mail.SendAsync(
    new AsterloomMailMessage(
        smtpAccountId,
        ClientMessageId: $"verify-email:{userId}",
        To: [userEmail],
        Subject: "Confirm your email",
        TextBody: $"Your confirmation code is {code}",
        HtmlBody: $"<p>Your confirmation code is <strong>{encodedCode}</strong>.</p>"),
    cancellationToken);
```

`authenticatedHttpClient` must add `Authorization: Bearer <service access token>` to every Asterloom request. The client secret and service token stay in the business backend; they must never enter browser JavaScript.

The Reference App demonstrates the token handler and mail gateway in:

- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceServiceTokenHandler.cs`
- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceMailGateway.cs`
- `Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityEndpoints.cs`

Enable the sample with:

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

The SMTP account must belong to that tenant/application, and the confidential client must have `mail.delivery.send` in the same scope.

## JSON Transcoding API

Send content to:

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
  "subject": "Order confirmed",
  "textBody": "Your order is confirmed.",
  "htmlBody": "<p>Your order is <strong>confirmed</strong>.</p>"
}
```

The same `clientMessageId` in the same tenant/application returns the original delivery and does not submit a second SMTP message. Generate a stable ID from the business event being notified; do not generate a new ID every time an HTTP retry occurs.

## Delivery states and error handling

| Status | Meaning |
| --- | --- |
| `PENDING` | The idempotency reservation exists and SMTP delivery has not completed. |
| `SENT` | The SMTP server accepted the message. It does not guarantee inbox placement. |
| `FAILED` | Authentication, TLS, connection, protocol, or SMTP command processing failed. |

The C# SDK throws `AsterloomMailDeliveryException` for a `FAILED` result and exposes the complete delivery record through its `Delivery` property. Treat `SENT` as SMTP acceptance, not proof that the recipient read or received the message.

## Permissions and security

| Permission | Purpose |
| --- | --- |
| `mail.account.read` | List and inspect SMTP accounts without credentials. |
| `mail.account.create` | Add an SMTP account and authorization code. |
| `mail.account.update` | Change SMTP settings or replace the authorization code. |
| `mail.account.archive` / `mail.account.restore` | Control whether an account can send. |
| `mail.account.test` | Send a test message from the console. |
| `mail.delivery.read` | Inspect delivery metadata. |
| `mail.delivery.send` | Submit application email. |

Production must persist `Identity:DataProtectionKeysPath`. Losing or replacing the key ring makes existing SMTP credentials undecryptable; re-enter the authorization code to recover. Do not log request bodies for Mail endpoints because business content can contain personal or security-sensitive data.
