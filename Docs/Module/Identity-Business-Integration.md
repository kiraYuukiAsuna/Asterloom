# Integrating Business Applications with Passport

[English](Identity-Business-Integration.md) | [简体中文](Identity-Business-Integration.zh-CN.md) | [Identity](Identity.md)

This guide explains how products such as Business A and Business B use Asterloom as a unified Passport. Each product owns its end-user registration, sign-in, and recovery UI. Passwords, client secrets, and OIDC tokens travel only between the trusted business backend and Asterloom.

## 1. Account model

```text
Global Passport account (one user ID / sub)
  ├─ Application membership: Business A (active/removed)
  │    └─ Authorization bindings and policies for Business A
  └─ Application membership: Business B (active/removed)
       └─ Authorization bindings and policies for Business B
```

- A Passport account is global. One email has one account and one stable `sub`.
- Membership is application-specific. A user may join A but not B, or be removed only from A.
- Authorization role bindings and policies remain isolated by Tenant/Application/Environment scope.
- Administrators and business users are not stored in separate account systems. Administrators have trusted Passport roles; normal business users usually have an empty `roles` collection.
- Asterloom Web is the management control plane, not the end-user registration or sign-in UI for each product.

After registering in A, the user can reuse the same account in B. B can either enable `allow_membership_auto_join` so a successful first login joins B, or call the registration API from B's trusted backend. For an existing email, the correct password is required and only B's membership is added.

## 2. Resources required by each application

Use the Web console to:

1. Create or select a Tenant and Application under `/tenants`.
2. Create a Confidential Web Client in `/identity/users` under **OIDC clients**.
3. Bind the client to the exact `tenantId` and `applicationId`.
4. Enable `client_credentials` and `password`; add `refresh_token` for renewable sessions.
5. Enable `allow_user_registration` when this backend may register users.
6. Decide whether `allow_membership_auto_join` matches the product policy.
7. Save the one-time client secret and inject it from a secret manager.
8. Configure application-scoped role bindings or policies under `/authorization/roles`.

An OIDC client binds to exactly one Platform Application. Give A and B separate clients and secrets.

## 3. Registration flow

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

Headless JSON Transcoding endpoints:

| Operation | HTTP | Scope source |
| --- | --- | --- |
| Register or join this application | `POST /api/v1/identity/accounts:register` | Caller client binding |
| Confirm email | `POST /api/v1/identity/accounts:confirm-email` | Caller client binding |
| Read an account in this application | `GET /api/v1/identity/accounts/{userId}` | Caller client binding |
| Remove membership from this application | `DELETE /api/v1/identity/accounts/{userId}/membership?expectedVersion=...` | Caller client binding |

Requests cannot select a Tenant or Application, so a business backend cannot operate on another application's membership. Deliver the returned confirmation token through the product's email provider and confirmation page; never log or analyze it.

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
    email, displayName, password, cancellationToken);

// Deliver registration.EmailVerificationToken through the product's email service.
await accounts.ConfirmEmailAsync(email, confirmationToken, cancellationToken);
```

`RegisterAccountAsync` behaves as follows:

- New email: creates a Pending global account and this application's membership.
- Existing email with the correct password: reuses the account and creates or restores only this membership.
- Wrong password, or a Suspended/Archived account: rejects the request to prevent account takeover.
- Confirmation changes a Pending account to Active. An already confirmed global account does not confirm again when joining another application.

## 4. Sign-in and BFF sessions

The browser submits credentials only to its same-origin business backend. That backend uses its Confidential Client for the controlled Password Grant:

```csharp
var tokens = await identity.AuthenticateWithPasswordAsync(
    email, password, cancellationToken);
```

The token contains the stable global `sub` plus the bound `tenant_id`, `application_id`, and `asterloom_actor_type=user`. A and B receive the same `sub` but different `application_id` values.

The business backend must:

1. Encrypt Access/Refresh Tokens in a server-side session store (Redis or a database in production).
2. Give the browser only a random opaque `HttpOnly`, `Secure`, appropriately `SameSite` session cookie.
3. Store tokens per browser session; never put user tokens in a singleton `IAsterloomTokenStore`.
4. Call `RefreshUserTokensAsync(currentTokens)` near expiration and atomically replace the session.
5. Clear the session and require sign-in after refresh or API authentication failure.

Never expose Password Grant directly to a browser. Never place the client secret, Access Token, or Refresh Token in JavaScript, Local Storage, plaintext cookies, logs, Telemetry, or Analytics.

## 5. Membership and permission invalidation

Access requires both conditions:

```text
active application membership
  AND Authorization decision allows permission
  → request allowed
```

- Removing a membership immediately blocks protected API calls made with that application's bound user token and also blocks refresh.
- If the client enables `allow_membership_auto_join`, the next successful password sign-in reactivates membership. Disable auto-join, globally suspend the account, or add an Authorization Deny when removal must remain enforced.
- Other applications' memberships, sign-in, and permissions continue to work.
- Suspending or archiving the global account blocks sign-in and refresh for every application.
- Removing a role binding or adding a Deny policy changes permission without deleting membership.
- Restoring membership does not recreate a previously removed role binding.

## 6. Management console responsibility

`/identity/users` covers the complete management API:

- Global account create/invite, profile, Passport roles, password reset, suspend, archive, and restore.
- OIDC session listing and revocation.
- Application membership list/filter, add/restore, and remove.
- OIDC client application bindings, grants, registration/auto-join switches, redirects, scopes, secret rotation, and deletion.
- OIDC scope management.

The console does not impersonate a business client to invoke Headless APIs because that would require retaining each product's client secret. Its administrative account and membership actions provide the corresponding manual operations.

## 7. Runnable reference

The reference backend demonstrates the BFF boundary:

- [ReferenceIdentityEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityEndpoints.cs)
- [ReferenceIdentityGateway.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentityGateway.cs)
- [ReferenceIdentitySessionStore.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceIdentitySessionStore.cs)

After configuring and starting it:

```powershell
$env:ASTERLOOM_REFERENCE_ACCOUNT_PASSWORD = "Use-A-Strong-Test-Password!2026"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- \
  account-demo user@example.com "Example User"
dotnet run --project Backend/Samples/Asterloom.ReferenceApp.Client -- \
  account-login user@example.com
```

`account-demo` performs real registration, email confirmation, sign-in, server-side session inspection, and sign-out. Its in-memory session store is illustrative only; production requires a shared, encrypted, expiring, revocable store.
For a local demo without email delivery, explicitly enable `ASTERLOOM_REFERENCE_EXPOSE_CONFIRMATION_TOKEN=true`.
This returns the confirmation token to the test client, is disabled by default, and must not be enabled on a public production endpoint.

## 8. Related contracts

- Business access protocol: [identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- Management protocol: [identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- C# business account SDK: [AsterloomIdentityAccessClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityAccessClient.cs)
- Sign-in SDK: [AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- Authorization integration: [Authorization.md](Authorization.md)
