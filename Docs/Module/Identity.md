# Identity: Passport, Users, and OIDC Clients

[English](Identity.md) | [简体中文](Identity.zh-CN.md) | [Module index](README.md)

Identity establishes who a caller is: user accounts, invitations, login sessions, OIDC/OAuth 2.0 clients,
scopes, token acquisition, and sign-out. [Authorization](Authorization.md) decides what that identity may do.

## 1. Authentication modes

| Caller | Flow | Client type | Secret |
| --- | --- | --- | --- |
| Desktop/native client | Authorization Code + PKCE | Public / Native | Never embedded |
| Web BFF | Authorization Code | Confidential / Web | BFF server only |
| Backend service, job, CI | Client Credentials | Confidential | Secret Manager injection |

Standard endpoints:

- `/connect/authorize`
- `/connect/token`
- `/connect/logout`
- `/.well-known/openid-configuration`
- `/passport/invitation`

Use a fixed HTTPS Issuer in production. Token `iss` must match it exactly.

## 2. Web administration

Route: `/identity/users`

One workspace covers three resource groups.

### Users

- List/Get users, search, and include archived records.
- Invite and resend invitations with expiring links.
- Update display name and Passport role.
- Suspend/Reactivate and Archive/Restore.
- List sessions and revoke one or all sessions.

### Clients

- List/Get/Create/Update/Delete OIDC clients.
- Configure application type, client type, grants, redirect URIs, post-logout URIs, and scopes.
- A confidential client secret is returned only when created or rotated; copy it immediately.

### Scopes

- List/Get/Create/Update/Delete scopes.
- Scopes limit what a token requests; Authorization still decides each API permission.

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

Never commit the client secret to source, an image, or `appsettings.json`; inject it from a secret manager.

## 4. Interactive desktop sign-in

Create a public native client with Authorization Code and a registered loopback redirect:

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

The SDK uses the system browser and PKCE. A public client must not have a secret. The default
`AsterloomInMemoryTokenStore` is for examples; production desktop applications should implement
`IAsterloomTokenStore` with DPAPI, Keychain, or another OS-protected store.

## 5. Token lifecycle

- `GetAccessTokenAsync` reuses a valid token and refreshes or reacquires it near expiration.
- An interactive client needs `offline_access` to receive a refresh token.
- `SignOutAsync` calls the end-session endpoint and clears local tokens.
- `ClearLocalSessionAsync` clears only local state and does not revoke other server sessions.
- After administrative revocation, return to sign-in when the next API or refresh operation fails.

## 6. Permissions

- Users: `identity.user.read/invite/update/roles.set/suspend/reactivate/archive/restore`
- Sessions: `identity.session.read/revoke`
- Clients: `identity.client.read/create/update/secret.rotate/delete`
- Scopes: `identity.scope.read/create/update/delete`

Client and scope registration does not grant business access. Configure Platform membership and Authorization
roles, bindings, or policies as well.

## 7. Security checklist

- Register redirect URIs exactly; avoid open wildcards and uncontrolled redirects.
- The SDK rejects non-loopback HTTP issuers and redirects; use HTTPS in production.
- Native clients never carry a secret; service secrets must be rotatable and revocable.
- A browser stores only an HttpOnly session ID; OIDC tokens remain in the BFF.
- Never place access or refresh tokens in logs, Analytics, Telemetry, or browser local storage.
- Treat invitation links and one-time secrets as credentials.

## 8. Related implementation

- Admin protocol: [identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- Types: [identity_types.proto](../../Proto/Asterloom/identity/v1/identity_types.proto)
- C# client: [AsterloomIdentityClient.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityClient.cs)
- SDK registration: [AsterloomIdentityServiceCollectionExtensions.cs](../../Backend/Asterloom.Sdk.Identity/AsterloomIdentityServiceCollectionExtensions.cs)
- Server module: [IdentityModule.cs](../../Backend/Asterloom.Module.Identity/IdentityModule.cs)
- Web: [identity-workspace.tsx](../../Frontend/features/identity/identity-workspace.tsx)
- BFF guide: [Web-Console-Bff.md](Web-Console-Bff.md)
