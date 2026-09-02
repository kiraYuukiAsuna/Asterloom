# Integrating Business Applications with Passport

[English](Identity-Business-Integration.md) | [简体中文](Identity-Business-Integration.zh-CN.md) | [Identity](Identity.md)

This is the standard Asterloom business integration. A desktop, mobile, or other browser-capable native application signs in with a Public Client using Authorization Code + PKCE. It then sends the resulting user Access Token directly to its business API. The business backend validates the token signature and its own audience locally; it neither stores the user token nor exchanges a token with its Client Secret on every request.

A Confidential Client is still required for trusted backend operations such as account registration, email confirmation, service jobs, and administration. It must never be shipped in a client application.

## 1. Account and application model

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

- Passport accounts are global; A and B receive the same `sub` for the same person.
- Membership, the token's `application_id`, and authorization scope are application-specific.
- Administrators and business users share the same account store. Administrators simply have trusted Passport roles.
- Asterloom Web is the control plane. Products own their end-user UI; Passport authenticates the user during the Public Client redirect.

## 2. Resources for one business

Create or select a Tenant/Application in the Web console, then create:

1. An API scope such as `business-a.api` whose resource is the unique business API audience `business-a-api`.
2. A Public Native Client bound to that Tenant/Application. Enable `authorization_code`, `refresh_token`, and PKCE; grant `openid profile email roles offline_access asterloom.api business-a.api`; register exact redirect URIs. A Public Client has no secret.
3. A Confidential Web Client bound to the same application. Enable `client_credentials` and `allow_user_registration` when the backend registers users. If a browser BFF signs users in, create a separate Web Client with `authorization_code` and `refresh_token`.

`allow_membership_auto_join` determines whether an existing global account joins this application after its first successful login. A and B must not share client IDs, secrets, or API audiences.

## 3. Public Client sign-in and API call

`Asterloom.Sdk.Identity` opens the system browser, hosts a loopback callback, and applies PKCE:

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

Only the Access Token is an API bearer credential. The ID Token describes the sign-in result; the Refresh Token is presented only to Passport. Neither is accepted by a business API.

Native apps store tokens in operating-system secure storage and refresh through the SDK. Never place them in configuration files, command lines, logs, Telemetry, or Analytics. A native Public Client cannot protect a secret and must not be assigned one.

## 4. ASP.NET Core resource server

Reference `Asterloom.Sdk.Identity.AspNetCore` from the business backend:

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

Using standard OIDC discovery and JWKS, the SDK validates RS256, `typ=at+jwt`, issuer, audience, expiry, a stable `sub`, `asterloom_actor_type=user`, and the configured tenant/application binding. Ordinary API calls therefore need neither a business Client Secret nor an Asterloom round trip. Signing metadata refreshes through the standard middleware.

Production endpoints require HTTPS. Plain HTTP is restricted to an explicitly enabled loopback development issuer.

### Real-time Asterloom permission checks

An endpoint that relies on Asterloom roles or policies can add a remote permission policy:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("platform-read", policy => policy
        .RequireAuthenticatedUser()
        .RequireAsterloomPermission("platform.info.read"));

app.MapGet("/platform", Handle)
    .RequireAuthorization("platform-read");
```

The handler forwards the current request's same user Access Token to Asterloom `CheckPermission`. Asterloom takes `sub` and application scope from the token and checks membership and current role/policy state. The backend still does not persist the token. A failed or unavailable permission service fails closed.

## 5. Trusted backend account registration

Registration belongs to the business backend, not its Public Client. The backend gets a Service Token with its Confidential Client and invokes the account API:

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

The transport URI is the Asterloom API address, not a second client identity setting. `Issuer` drives OIDC and tokens; the transport address is where gRPC/HTTP API requests are sent. They are normally the same in a single-domain deployment.

The API is locked to the Confidential Client's application binding. A new email creates a global account. An existing email plus the correct password reuses its `sub` and adds or restores only this application's membership.

## 6. Web/BFF and token ownership

For a browser product, prefer Authorization Code + PKCE through its BFF. The BFF completes the callback, encrypts user tokens in a server-side session store, and gives the browser only a random `HttpOnly`, `Secure` session cookie. Here the BFF stores and refreshes one user token set per browser session.

The native Public Client flow differs: the client securely stores and refreshes tokens; the business backend only validates each Access Token. A Confidential Client Service Token is a third identity and cannot replace a user token on an endpoint that needs user context.

Password Grant is disabled. Neither native clients nor browser BFFs may collect Passport passwords and exchange them directly at the Token Endpoint.

## 7. Invalidation boundaries

- Access Tokens live for 10 minutes by default. A resource server using only local `.RequireAuthorization()` does not learn about membership, account, or policy changes until the token expires.
- Passport rechecks the account and membership during refresh, so refresh fails immediately after membership removal.
- `RequireAsterloomPermission` checks membership and permissions on every request and therefore applies changes immediately.
- Use remote permission policies on high-risk operations; use the short local-token window where appropriate for lower-risk calls.
- Different audiences and application bindings prevent an A token from being accepted by B.

## 8. Runnable reference and tests

`Provision-Reference-App.sh` creates `asterloom.reference.api` → `asterloom-reference-api`, an application-bound `asterloom-reference-native` Public Client, Confidential registration/service clients, and Reference Backend resource-server configuration.

`Asterloom.ReferenceApp.Client login` completes PKCE sign-in and calls `/api/reference/me` with the resulting Access Token. See:

- [ReferenceProtectedEndpoints.cs](../../Backend/Samples/Asterloom.ReferenceApp.Backend/ReferenceProtectedEndpoints.cs)
- [Reference resource-server setup](../../Backend/Samples/Asterloom.ReferenceApp.Backend/Program.cs)
- [Reference Public Client](../../Backend/Samples/Asterloom.ReferenceApp.Client/Program.cs)
- [ASP.NET Core resource-server SDK](../../Backend/Asterloom.Sdk.Identity.AspNetCore/AsterloomResourceServerServiceCollectionExtensions.cs)

Integration tests verify that the Access Token is a public three-part `at+jwt`, its signing key exists in JWKS, a resource server accepts it, an ID Token is rejected, and the same user token can drive a remote permission decision.

## 9. Related contracts

- [identity_access.proto](../../Proto/Asterloom/identity/v1/identity_access.proto)
- [identity_admin.proto](../../Proto/Asterloom/identity/v1/identity_admin.proto)
- [authorization.proto](../../Proto/Asterloom/authorization/v1/authorization.proto)
- [Authorization guide](Authorization.md)
