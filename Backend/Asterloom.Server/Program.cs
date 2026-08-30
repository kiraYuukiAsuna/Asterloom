using Asterloom.Modules.Hosting;
using Asterloom.Modules.Analytics;
using Asterloom.Modules.Auditing;
using Asterloom.Modules.Authorization;
using Asterloom.Modules.Config;
using Asterloom.Modules.Feature;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Identity;
using Asterloom.Modules.Platform;
using Asterloom.Modules.Rpc;
using Asterloom.Modules.Rpc.Operations;
using Asterloom.Modules.Release;
using Asterloom.Modules.Targeting;
using Asterloom.Modules.Storage;
using Asterloom.Modules.Telemetry;
using Asterloom.Sdk.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

var telemetryOptions = AsterloomTelemetryOptions.FromConfiguration(
    builder.Configuration,
    "Asterloom.Server",
    typeof(Program).Assembly.GetName().Version?.ToString(3));
builder.Services.AddAsterloomTelemetry(telemetryOptions);
builder.Logging.AddAsterloomTelemetryLogging(telemetryOptions);

builder.Services.AddAsterloomRpc();
builder.Services.AddAsterloomModules(
    builder.Configuration,
    new PlatformModule(),
    new AuthorizationModule(),
    new AuditModule(),
    new TargetingModule(),
    new FeatureModule(),
    new ConfigModule(),
    new StorageModule(),
    new ReleaseModule(),
    new AnalyticsModule(),
    new TelemetryModule(),
    new OperationsModule(),
    new InfrastructureModule(),
    new IdentityModule(builder.Environment));
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: ["live", "ready", "startup"]);

var app = builder.Build();

app.UseAsterloomRpcFoundation();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapAsterloomModules();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = static registration => registration.Tags.Contains("live"),
    });
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = static registration => registration.Tags.Contains("ready"),
    });
app.MapHealthChecks(
    "/health/startup",
    new HealthCheckOptions
    {
        Predicate = static registration => registration.Tags.Contains("startup"),
    });
app.MapGet(
    "/",
    (IHostEnvironment environment) => environment.IsDevelopment()
        ? Results.Redirect("/swagger")
        : Results.Ok(new { name = "Asterloom.Server", status = "operational" }));

app.Run();

public partial class Program;
