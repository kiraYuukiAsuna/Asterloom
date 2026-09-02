# Identity: Global Passport Accounts and Application Access

[English](Identity.md) | [简体中文](Identity.zh-CN.md) | [Module index](README.md)

Identity establishes who a caller is: global user accounts, per-application membership, invitations, login sessions,
OIDC/OAuth 2.0 clients, scopes, token acquisition, and sign-out. [Authorization](Authorization.md) decides what that
identity may do. A control-plane administrator and a normal business user live in the same Passport account system;
trusted Passport roles distinguish operators, while business access is scoped through memberships and Authorization.

## 1. Authentication modes

| Caller | Flow | Client type | Secret |
| --- | --- | --- | --- |
| Desktop/native client | Authorization Code + PKCE | Public / Native | Never embedded |
| Management Web BFF | Authorization Code | Confidential / Web | BFF server only |
| Business backend registering accounts | Client Credentials | Confidential / Web, application-bound | Business backend only |
| Backend service, job, CI | Client Credentials | Confidential | Secret Manager injection |

Standard endpoints:

- `/connect/authorize`
- `/connect/token`
- `/connect/userinfo`
- `/connect/logout`
- `/.well-known/openid-configuration`
- `/passport/invitation`

Trusted business-backend endpoints are exposed through both native gRPC and JSON Transcoding at
`/api/v1/identity/accounts*`. See [business application identity integration](Identity-Business-Integration.md).

Use a fixed HTTPS Issuer in production. Token `iss` must match it exactly.

## 2. Web administration

Route: `/identity/users`

One workspace covers four resource groups.

### Users

- List/Get global users, search, and include archived records.
- Create an account directly or invite an operator through an expiring activation link.
- Invite and resend invitations with expiring links.
- Update display name and Passport role.
- Reset a password and revoke the affected sessions.
- Suspend/Reactivate and Archive/Restore.
- List sessions and revoke one or all sessions.

### Application memberships

- List/filter memberships by user, Tenant, or Application, including removed records.
- Add or restore an existing global account in one Platform Application.
- Remove access to one application without deleting the global account or other memberships.

### Clients

- List/Get/Create/Update/Delete OIDC clients.
- Configure application type, client type, grants, redirect URIs, post-logout URIs, and scopes.
- Optionally bind a client to one Tenant/Application and control trusted registration and login auto-join.
- A confidential client secret is returned only when created or rotated; copy it immediately.
- The bootstrap `Asterloom Web Console` client is a system resource used by the management BFF. It is marked
  `isSystem=true` and `isMutable=false`; the API rejects update, secret rotation, and deletion. Change its callback
  URLs or secret through deployment configuration and rerun the migration/bootstrap service.

### Scopes

- List/Get/Create/Update/Delete scopes.
- Scopes limit what a token requests; Authorization still decides each API permission.
- The bootstrap `asterloom.api` scope is a system resource and cannot be updated or deleted.

The Web Console displays system resources as read-only and does not render destructive actions. These restrictions
are enforced again by the backend; hiding a button is not the security boundary.

A Passport role is a platform account role, not a customizable Authorization role. Prefer Authorization roles,
bindings, and policies for business access.

## 3. Service authentication

Create a confidential client with Client Credentials and the `asterloom.api` scope:

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

Never commit the client secret to source, an image, or `appsettings.json`; inject it from a secret manager. An
unbound service client represents a platform service. A business client must be bound to one Platform Application.

## 4. Business application registration and sign-in

The standard native flow uses Authorization Code + PKCE in a Public Client and sends the resulting user Access Token
as a bearer credential directly to the business backend. `Asterloom.Sdk.Identity.AspNetCore` validates its signature,
issuer, business audience, and tenant/application binding. The backend neither stores the user token nor substitutes
its own Client Secret for the user identity.

Account registration still runs from a trusted backend through its Confidential Client and
`AsterloomIdentityAccessClient`. A browser product uses Authorization Code + S256 PKCE through an OIDC BFF, which
encrypts one user token set per browser session and returns only an opaque HttpOnly cookie. A business backend must
not collect Passport passwords to exchange them directly for tokens.

The same global account has the same `sub` in every business application, while each application receives its own
`application_id` and audience. Follow the complete setup, invalidation boundaries, and C# example in
[Integrating Business Applications with Passport](Identity-Business-Integration.md).

## 5. Interactive desktop sign-in

Create a public native client with Authorization Code and a registered loopback redirect:

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
// Send accessToken, not the ID Token, as the business API bearer credential.
await identity.SignOutAsync(cancellationToken);
```

The SDK uses the system browser and PKCE. A public client must not have a secret. The default
`AsterloomInMemoryTokenStore` is for examples; production desktop applications should implement
`IAsterloomTokenStore` with DPAPI, Keychain, or another OS-protected store.

## 6. Token lifecycle

- Without **Keep me signed in**, Passport uses a browser-session cookie that expires when the browser closes. With it,
  Passport and the management Web BFF session persist for 30 days. Explicit logout immediately clears both layers.
- `GetAccessTokenAsync` reuses a valid token and refreshes or reacquires it near expiration.
- An interactive client needs `offline_access` to receive a refresh token.
- `SignOutAsync` calls the end-session endpoint and clears local tokens.
- `ClearLocalSessionAsync` clears only local state and does not revoke other server sessions.
- After administrative revocation, return to sign-in when the next API or refresh operation fails.
- Refresh rechecks active membership. A third-party API doing only local JWT validation has an invalidation window no longer than the Access Token lifetime.
- Resource-server policies using `RequireAsterloomPermission` check membership and roles/policies remotely on every request.

## 7. Permissions

- Users: `identity.user.read/create/invite/update/roles.set/password.reset/suspend/reactivate/archive/restore`
- Sessions: `identity.session.read/revoke`
- Memberships: `identity.application-membership.read/set/remove`
- Clients: `identity.client.read/create/update/secret.rotate/delete`
- Scopes: `identity.scope.read/create/update/delete`

Client and scope registration does not grant business access. Configure Platform membership and Authorization
roles, bindings, or policies as well.

## 8. Security checklist

- Register redirect URIs exactly; avoid open wildcards and uncontrolled redirects.
- The SDK rejects non-loopback HTTP issuers and redirects; use HTTPS in production.
- Native clients never carry a secret; service secrets must be rotatable and revocable.
- A browser stores only an HttpOnly session ID; OIDC tokens remain in the BFF.
- Password Grant and Implicit Grant are disabled; user sign-in uses only Authorization Code + S256 PKCE.
- Use a separate client and secret for each business application.
- Never place access or refresh tokens in logs, Analytics, Telemetry, or browser local storage.
- Treat invitation links and one-time secrets as credentials.

## 9. Related implementation

- Admin protocol: [identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- Business access protocol: [identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- Types: [identity_types.proto](../../Proto/Asterloom/identity/v1/identity_types.proto)
- C# client: [AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- Business account client: [AsterloomIdentityAccessClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityAccessClient.cs)
- SDK registration: [AsterloomIdentityServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityServiceCollectionExtensions.cs)
- ASP.NET Core resource-server SDK: [AsterloomResourceServerServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity.AspNetCore/AsterloomResourceServerServiceCollectionExtensions.cs)
- Server module: [IdentityModule.cs](../../Backend/Asterloom.Module.Identity/IdentityModule.cs)
- Web: [identity-workspace.tsx](../../Frontend/features/identity/identity-workspace.tsx)
- BFF guide: [Web-Console-Bff.md](Web-Console-Bff.md)
- Business integration guide: [Identity-Business-Integration.md](Identity-Business-Integration.md)
