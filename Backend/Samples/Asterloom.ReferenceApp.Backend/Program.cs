using System.Diagnostics;
using System.Diagnostics.Metrics;
using Asterloom.ReferenceApp.Backend;
using Asterloom.Sdk.Identity;
using Asterloom.Sdk.Identity.AspNetCore;
using Asterloom.Sdk.Mail;
using Asterloom.Sdk.Telemetry;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc().AddJsonTranscoding();
builder.Services.AddSingleton(ReferenceAppInstrumentation.Instance);
builder.Services.AddSingleton(provider =>
{
    var connectionString = builder.Configuration.GetConnectionString("ReferenceApp");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:ReferenceApp is required for the reference backend.");
    }

    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddSingleton<ReferenceAppStore>();
var referenceIdentity = ReferenceIdentityOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(referenceIdentity);
var resourceServer = ReferenceResourceServerOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(resourceServer);
var referenceMail = ReferenceMailOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(referenceMail);
if (referenceIdentity.Enabled)
{
    builder.Services.AddAsterloomIdentityClient(options =>
    {
        options.Issuer = referenceIdentity.PassportIssuer;
        options.ClientId = referenceIdentity.ClientId;
        options.ClientSecret = referenceIdentity.ClientSecret;
        options.RegistrationId = "asterloom-reference-business-backend";
        options.EnableServiceCredentials = true;
        options.AllowInsecureHttpForDevelopment =
            referenceIdentity.AllowInsecureHttpForDevelopment;
    });
    builder.Services.AddSingleton<ReferenceIdentityGateway>();
    if (referenceMail.Enabled)
    {
        builder.Services.AddTransient<ReferenceServiceTokenHandler>();
        builder.Services.AddHttpClient<ReferenceMailGateway>(client =>
        {
            client.BaseAddress = referenceIdentity.AsterloomBaseAddress;
        }).AddHttpMessageHandler<ReferenceServiceTokenHandler>();
    }
}
if (resourceServer.Enabled)
{
    builder.Services.AddAsterloomResourceServer(options =>
    {
        options.Issuer = resourceServer.Issuer;
        options.AuthorizationServer = resourceServer.AuthorizationServer;
        options.Audience = resourceServer.Audience;
        options.TenantId = resourceServer.TenantId;
        options.ApplicationId = resourceServer.ApplicationId;
        options.AllowInsecureHttpForDevelopment =
            resourceServer.AllowInsecureHttpForDevelopment;
    });
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(
            ReferenceProtectedEndpoints.PlatformReadPolicy,
            policy => policy
                .RequireAuthenticatedUser()
                .RequireAsterloomPermission("platform.info.read"));
}
if (referenceMail.Enabled && !referenceIdentity.Enabled)
{
    throw new InvalidOperationException(
        "Asterloom:Identity must be enabled when Asterloom:Mail is enabled.");
}

var telemetry = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    "asterloom.reference.backend",
    typeof(Program).Assembly.GetName().Version?.ToString());
telemetry.ActivitySourceNames.Add(ReferenceAppInstrumentation.ActivitySourceName);
telemetry.MeterNames.Add(ReferenceAppInstrumentation.MeterName);
telemetry.TenantId = builder.Configuration["Asterloom:TenantId"] ?? string.Empty;
telemetry.ApplicationId = builder.Configuration["Asterloom:ApplicationId"] ?? string.Empty;
telemetry.EnvironmentId = builder.Configuration["Asterloom:EnvironmentId"] ?? string.Empty;
builder.Services.AddAsterloomTelemetry(telemetry);
builder.Logging.AddAsterloomTelemetryLogging(telemetry);

var app = builder.Build();

if (resourceServer.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

await app.Services.GetRequiredService<ReferenceAppStore>().InitializeAsync(
    app.Lifetime.ApplicationStopping);

app.MapGrpcService<ReferenceAppGrpcService>();
if (referenceIdentity.Enabled)
{
    app.MapReferenceIdentityEndpoints();
}
if (resourceServer.Enabled)
{
    app.MapReferenceProtectedEndpoints();
}
if (referenceMail.Enabled && resourceServer.Enabled)
{
    app.MapReferenceMailEndpoints();
}
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    service = "asterloom.reference.backend",
    identityBffEnabled = referenceIdentity.Enabled,
    resourceServerEnabled = resourceServer.Enabled,
    mailEnabled = referenceMail.Enabled,
}));

app.Run();

public partial class Program;
