using Asterloom.Modules.Errors;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Telemetry;
using Asterloom.Sdk.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class TelemetryTests
{
    [Fact]
    public async Task SourcesAndSettingsUseValidationAndOptimisticConcurrency()
    {
        await using var provider = CreateManagementProvider();
        using var scope = provider.CreateScope();
        var management = scope.ServiceProvider.GetRequiredService<TelemetryManagementService>();
        var tenantId = Guid.NewGuid().ToString();
        var applicationId = Guid.NewGuid().ToString();
        var environmentId = Guid.NewGuid().ToString();

        var source = await management.CreateSourceAsync(
            tenantId,
            applicationId,
            environmentId,
            "checkout-api",
            "Checkout API",
            "Checkout technical signals.",
            "asterloom.checkout-api",
            "{\"team.name\":\"payments\"}",
            CancellationToken.None);
        Assert.Equal(1, source.Version);

        var conflict = await Assert.ThrowsAsync<AsterloomException>(() =>
            management.UpdateSourceAsync(
                tenantId,
                applicationId,
                environmentId,
                source.Id.ToString(),
                source.DisplayName,
                source.Description,
                source.ServiceName,
                source.ResourceAttributesJson,
                expectedVersion: 0,
                CancellationToken.None));
        Assert.Equal("concurrency_conflict", conflict.ErrorCode);

        var reservedAttribute = await Assert.ThrowsAsync<AsterloomException>(() =>
            management.CreateSourceAsync(
                tenantId,
                applicationId,
                environmentId,
                "worker",
                "Worker",
                string.Empty,
                "asterloom.worker",
                "{\"service.name\":\"spoofed\"}",
                CancellationToken.None));
        Assert.Equal("validation_failed", reservedAttribute.ErrorCode);

        var settings = await management.GetSettingsAsync(
            tenantId,
            applicationId,
            environmentId,
            CancellationToken.None);
        Assert.Equal(0, settings.Version);
        settings = await management.UpdateSettingsAsync(
            tenantId,
            applicationId,
            environmentId,
            samplingRatio: 0.1,
            tracesEnabled: true,
            metricsEnabled: true,
            logsEnabled: false,
            exporterEndpoint: "http://collector:4318",
            exporterProtocol: Asterloom.Modules.Telemetry.Model.TelemetryOtlpProtocol.HttpProtobuf,
            diagnosticsBaseUrl: "https://observability.example/traces",
            expectedVersion: 0,
            cancellationToken: CancellationToken.None);
        Assert.Equal(1, settings.Version);
        Assert.Equal(0.1, settings.SamplingRatio);
    }

    [Fact]
    public void SdkRegistersOfficialOpenTelemetryProvidersAndRejectsInvalidSampling()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var options = new AsterloomTelemetryOptions
        {
            ServiceName = "Asterloom.UnitTests",
            ExporterEndpoint = new Uri("http://localhost:4317"),
            LogsEnabled = false,
        };
        options.ActivitySourceNames.Add("Asterloom.UnitTests");
        options.MeterNames.Add("Asterloom.UnitTests");
        services.AddAsterloomTelemetry(options);
        using var provider = services.BuildServiceProvider();
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName?.Contains(
                "TracerProvider",
                StringComparison.Ordinal) == true);

        options.ExporterEndpoint = new Uri("http://localhost:4318/otlp");
        options.ExporterProtocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
        Assert.Equal(
            new Uri("http://localhost:4318/otlp/v1/traces"),
            AsterloomTelemetryServiceCollectionExtensions.ResolveExporterEndpoint(options, "traces"));

        options.SamplingRatio = 1.1;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddAsterloomTelemetry(options));
    }

    private static ServiceProvider CreateManagementProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "Memory",
                ["Telemetry:CollectorHealthEndpoint"] = "http://localhost:13133/",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        new TelemetryModule().AddServices(services, configuration);
        new InfrastructureModule().AddServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
