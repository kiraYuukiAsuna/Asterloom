using System.Diagnostics;
using System.Diagnostics.Metrics;
using Asterloom.ReferenceApp.Backend;
using Asterloom.Sdk.Identity;
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
if (referenceIdentity.Enabled)
{
    builder.Services.AddAsterloomIdentityClient(options =>
    {
        options.Issuer = referenceIdentity.PassportIssuer;
        options.ClientId = referenceIdentity.ClientId;
        options.ClientSecret = referenceIdentity.ClientSecret;
        options.RegistrationId = "asterloom-reference-business-backend";
        options.EnableServiceCredentials = true;
        options.EnablePasswordAuthentication = true;
        options.RequestRefreshTokens = true;
        options.AllowInsecureHttpForDevelopment =
            referenceIdentity.AllowInsecureHttpForDevelopment;
    });
    builder.Services.AddSingleton<ReferenceIdentityGateway>();
    builder.Services.AddSingleton<ReferenceIdentitySessionStore>();
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

await app.Services.GetRequiredService<ReferenceAppStore>().InitializeAsync(
    app.Lifetime.ApplicationStopping);

app.MapGrpcService<ReferenceAppGrpcService>();
if (referenceIdentity.Enabled)
{
    app.MapReferenceIdentityEndpoints();
}
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    service = "asterloom.reference.backend",
    identityBffEnabled = referenceIdentity.Enabled,
}));

app.Run();

public partial class Program;
