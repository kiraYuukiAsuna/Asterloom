using System.Diagnostics;
using System.Diagnostics.Metrics;
using Asterloom.ReferenceApp.Backend;
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
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    service = "asterloom.reference.backend",
}));

app.Run();

public partial class Program;
