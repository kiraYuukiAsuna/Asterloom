using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Asterloom.ReferenceApp.Protocol.V1;
using Asterloom.Sdk.Analytics;
using Asterloom.Sdk.Authorization;
using Asterloom.Sdk.Config;
using Asterloom.Sdk.Feature;
using Asterloom.Sdk.Release;
using Asterloom.Sdk.Rpc;
using Asterloom.Sdk.Storage;
using Asterloom.Sdk.Targeting;
using Asterloom.Targeting;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using OpenFeature.Model;

namespace Asterloom.ReferenceApp.Client;

internal sealed class ReferenceDiagnosticRunner(
    ReferenceAppSettings settings,
    ReferenceAppState state,
    AsterloomAuthenticatedTransport platform,
    ReferenceClientInstrumentation instrumentation,
    ILogger<ReferenceDiagnosticRunner> logger)
{
    private static readonly (string Name, string Component)[] CollectorSignalCounters =
    [
        ("otelcol_receiver_accepted_spans", "receiver=\"otlp/reference\""),
        ("otelcol_receiver_accepted_metric_points", "receiver=\"otlp/reference\""),
        ("otelcol_receiver_accepted_log_records", "receiver=\"otlp/reference\""),
        ("otelcol_exporter_sent_spans", "exporter=\"otlp_http/database\""),
        ("otelcol_exporter_sent_metric_points", "exporter=\"otlp_http/database\""),
        ("otelcol_exporter_sent_log_records", "exporter=\"otlp_http/database\""),
    ];
    private static readonly Action<ILogger, string, Exception?> LogTelemetryProbe =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "ReferenceTelemetryProbe"),
            "Reference telemetry probe for run {RunId}.");

    private readonly List<DiagnosticResult> _results = [];
    private ClientHeartbeat? _grpcHeartbeat;

    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(
        CancellationToken cancellationToken)
    {
        await RunStepAsync("Identity", TestIdentityAsync, cancellationToken);
        await RunStepAsync("Authorization", TestAuthorizationAsync, cancellationToken);
        await RunStepAsync("Targeting", TestTargetingAsync, cancellationToken);
        await RunStepAsync("Feature flags / rollout", TestFeatureAsync, cancellationToken);
        await RunStepAsync("Dynamic configuration", TestConfigAsync, cancellationToken);
        await RunStepAsync("Desktop updates", TestReleaseAsync, cancellationToken);
        await RunStepAsync("Analytics", TestAnalyticsAsync, cancellationToken);
        await RunStepAsync("Telemetry", TestTelemetryAsync, cancellationToken);
        await RunStepAsync("RPC", TestRpcAsync, cancellationToken);
        await RunStepAsync("HTTP transcoding", TestHttpTranscodingAsync, cancellationToken);
        await RunStepAsync("File storage", TestStorageAsync, cancellationToken);
        await RunStepAsync("Persistence", TestPersistenceAsync, cancellationToken);
        await RunStepAsync("Operations / OpenAPI", TestOperationsAsync, cancellationToken);
        return _results;
    }

    private async Task<string> TestIdentityAsync(CancellationToken cancellationToken)
    {
        using var discoveryClient = new HttpClient { BaseAddress = settings.PassportIssuer };
        using var discovery = await discoveryClient.GetAsync(
            ".well-known/openid-configuration",
            cancellationToken);
        await EnsureSuccessAsync(discovery, cancellationToken);
        using var clients = await platform.HttpClient.GetAsync(
            "api/v1/identity/clients?pageSize=1",
            cancellationToken);
        await EnsureSuccessAsync(clients, cancellationToken);
        return "Passport discovery, client-credentials token and protected Identity API succeeded.";
    }

    private async Task<string> TestAuthorizationAsync(CancellationToken cancellationToken)
    {
        var client = new AsterloomAuthorizationClient(platform.CallInvoker);
        var decision = await client.CheckPermissionAsync(
            "feature.flag.evaluate",
            Scope(),
            cancellationToken);
        if (!decision.Allowed)
        {
            throw new InvalidOperationException(
                $"Reference service identity lacks feature.flag.evaluate: {decision.Reason}");
        }

        return "Permission allowed via "
            + string.Join(',', decision.MatchedRoleKeys.DefaultIfEmpty("policy"))
            + ".";
    }

    private async Task<string> TestTargetingAsync(CancellationToken cancellationToken)
    {
        var client = new AsterloomTargetingAdminClient(platform.CallInvoker);
        var page = await client.ListSegmentsAsync(
            new AsterloomTargetingScope(state.TenantId, state.ApplicationId, state.EnvironmentId),
            pageSize: 100,
            cancellationToken: cancellationToken);
        if (!page.Items.Any(item => item.Id == state.SegmentId))
        {
            throw new InvalidOperationException("The provisioned targeting segment was not returned.");
        }

        return $"Targeting table/API returned {page.Items.Count} segment(s).";
    }

    private async Task<string> TestFeatureAsync(CancellationToken cancellationToken)
    {
        var provider = new AsterloomFeatureProvider(
            platform.CallInvoker,
            new AsterloomFeatureProviderOptions
            {
                Scope = new AsterloomFeatureScope(
                    state.TenantId,
                    state.ApplicationId,
                    state.EnvironmentId),
                CacheDuration = TimeSpan.Zero,
            });
        var context = EvaluationContext.Builder()
            .SetTargetingKey("reference-user-cn")
            .Set("region", "CN")
            .Build();
        var resolved = await provider.ResolveBooleanValueAsync(
            state.FeatureFlagKey,
            defaultValue: false,
            context,
            cancellationToken);
        if (!resolved.Value || resolved.Variant != "on")
        {
            throw new InvalidOperationException(
                $"Expected the CN targeting variant 'on', got '{resolved.Variant}'.");
        }

        return "Published OpenFeature value resolved to variant 'on' for the CN segment.";
    }

    private async Task<string> TestConfigAsync(CancellationToken cancellationToken)
    {
        var scope = new AsterloomConfigScope(
            state.TenantId,
            state.ApplicationId,
            state.EnvironmentId);
        using var client = new AsterloomConfigClient(
            platform.HttpClient,
            new AsterloomConfigClientOptions
            {
                Scope = scope,
                CacheDuration = TimeSpan.Zero,
                LastKnownGoodDuration = TimeSpan.FromMinutes(5),
            });
        var context = AsterloomConfigContext.Create(
            scope,
            "reference-user-cn",
            clientVersion: "0.0.0",
            platform: ReferenceAppProvisioner.GetRuntimeIdentifier(),
            region: "CN");
        var value = await client.GetStringAsync(
            state.ConfigKey,
            "fallback",
            context,
            cancellationToken);
        var snapshot = await client.GetSnapshotAsync(
            context,
            forceRefresh: true,
            cancellationToken: cancellationToken);
        if (!value.Contains("CN diagnostic", StringComparison.Ordinal)
            || !snapshot.Values.ContainsKey(state.ConfigKey))
        {
            throw new InvalidOperationException("The targeted dynamic configuration was not resolved.");
        }

        return $"Snapshot {snapshot.Version} resolved targeted value '{value}'.";
    }

    private async Task<string> TestReleaseAsync(CancellationToken cancellationToken)
    {
        var scope = new AsterloomReleaseScope(
            state.TenantId,
            state.ApplicationId,
            state.EnvironmentId);
        using var client = new AsterloomReleaseClient(
            platform.HttpClient,
            new AsterloomReleaseClientOptions
            {
                Scope = scope,
                TargetRuntimeId = state.ReleaseRuntimeId,
                PackageId = state.ReleasePackageId,
                TrustedPublicKeysByFingerprint = new Dictionary<string, string>
                {
                    [state.ReleaseSigningKeyFingerprint] = state.ReleasePublicKeyPem,
                },
                AllowInsecureDownloadUrls = settings.AllowInsecureDevelopment,
            });
        var decision = await client.CheckForUpdateAsync(
            state.ReleaseChannelKey,
            state.ReleaseBaselineVersion,
            AsterloomReleaseContext.Create(
                scope,
                "reference-update-client",
                clientVersion: state.ReleaseBaselineVersion,
                platform: state.ReleaseRuntimeId,
                region: "CN"),
            cancellationToken);
        if (!decision.UpdateAvailable)
        {
            throw new InvalidOperationException($"Expected an update: {decision.Reason}.");
        }

        if (!string.Equals(
                decision.Manifest!.ReleaseVersion,
                state.ReleaseTargetVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Expected release {state.ReleaseTargetVersion}, got {decision.Manifest.ReleaseVersion}.");
        }

        var downloads = decision.ArtifactDownloads;
        if (state.ReleaseHasDelta
            && (decision.SelectedArtifact?.ArtifactKind != AsterloomReleaseArtifactKind.Delta
                || !downloads.Any(static item =>
                    item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Full)
                || !downloads.Any(static item =>
                    item.Artifact.ArtifactKind == AsterloomReleaseArtifactKind.Delta)))
        {
            throw new InvalidDataException(
                "The real-package diagnostic did not receive both the selected Delta and Full fallback.");
        }

        var totalBytes = 0L;
        foreach (var download in downloads)
        {
            using var destination = new MemoryStream();
            await client.DownloadArtifactToAsync(
                decision,
                download.Artifact.Id,
                destination,
                cancellationToken: cancellationToken);
            if (destination.Length == 0)
            {
                throw new InvalidDataException(
                    $"The verified {download.Artifact.ArtifactKind} update artifact was empty.");
            }

            totalBytes += destination.Length;
        }

        return state.ReleaseHasDelta
            ? $"Signed {decision.Manifest.ReleaseVersion} Full+Delta pair downloaded and verified ({totalBytes} bytes)."
            : $"Signed {decision.Manifest.ReleaseVersion} Full update downloaded and verified ({totalBytes} bytes).";
    }

    private async Task<string> TestAnalyticsAsync(CancellationToken cancellationToken)
    {
        await using var client = new AsterloomAnalyticsClient(
            platform.HttpClient,
            new AsterloomAnalyticsClientOptions
            {
                WriteKey = state.AnalyticsWriteKey,
                BatchSize = 10,
                FlushInterval = TimeSpan.FromMinutes(1),
                MaximumRetries = 1,
                CompressionThresholdBytes = 1,
                CommonContext = new Dictionary<string, object?>
                {
                    ["platform"] = ReferenceAppProvisioner.GetRuntimeIdentifier(),
                    ["referenceRunId"] = state.RunId,
                },
            });
        await client.TrackAsync(
            "reference.diagnostic.completed",
            new { runId = state.RunId, success = true, durationMs = 0.0 },
            new AsterloomAnalyticsIdentity(
                ActorId: settings.ServiceClientId,
                SessionId: Guid.NewGuid().ToString("N")),
            cancellationToken: cancellationToken);
        var result = await client.FlushAsync(cancellationToken);
        if (result.Accepted != 1 || result.Remaining != 0)
        {
            throw new InvalidOperationException(
                $"Unexpected analytics result: accepted={result.Accepted}, remaining={result.Remaining}.");
        }

        return "One schema-validated analytics event was accepted.";
    }

    private async Task<string> TestTelemetryAsync(CancellationToken cancellationToken)
    {
        var metricsAddress = settings.TelemetryCollectorMetricsAddress
            ?? throw new InvalidOperationException(
                "ASTERLOOM_REFERENCE_OTEL_METRICS_URL is required to verify OTLP delivery.");
        var path = ScopePath() + "/telemetry";
        using var health = await platform.HttpClient.GetAsync(
            path + "/collector/health",
            cancellationToken);
        await EnsureSuccessAsync(health, cancellationToken);
        var healthBody = await health.Content.ReadAsStringAsync(cancellationToken);
        using var healthDocument = JsonDocument.Parse(healthBody);
        var healthStatus = healthDocument.RootElement.GetProperty("status").GetString();
        if (!string.Equals(
                healthStatus,
                "COLLECTOR_HEALTH_STATUS_HEALTHY",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Collector health is {healthStatus ?? "missing"}: {Compact(healthBody)}");
        }

        using var metricsClient = new HttpClient();
        var before = await ReadCollectorSignalCountersAsync(
            metricsClient,
            metricsAddress,
            cancellationToken);
        var emittedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        using (var activity = instrumentation.ActivitySource.StartActivity("reference.diagnostic.telemetry"))
        {
            activity?.SetTag("asterloom.reference.run_id", state.RunId);
            activity?.AddEvent(new ActivityEvent("reference.telemetry.probe"));
            LogTelemetryProbe(logger, state.RunId, null);
        }
        instrumentation.Diagnostics.Add(
            1,
            new KeyValuePair<string, object?>("capability", "Telemetry probe"),
            new KeyValuePair<string, object?>("outcome", "success"));

        using var sources = await platform.HttpClient.GetAsync(
            path + "/sources?pageSize=100",
            cancellationToken);
        await EnsureSuccessAsync(sources, cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        IReadOnlyDictionary<string, double> after;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            after = await ReadCollectorSignalCountersAsync(
                metricsClient,
                metricsAddress,
                cancellationToken);
            if (CollectorSignalCounters.All(counter =>
                    after[counter.Name] > before[counter.Name])
                && await HasStoredTelemetryAsync(path, emittedAt, cancellationToken))
            {
                return "Collector received, exported and stored Trace, Metric and Log through OTLP.";
            }
        }
        while (DateTimeOffset.UtcNow < deadline);

        var missing = CollectorSignalCounters
            .Where(counter => after[counter.Name] <= before[counter.Name])
            .Select(counter => counter.Name)
            .ToArray();
        if (missing.Length == 0)
        {
            throw new InvalidOperationException(
                "Collector exported telemetry, but the database did not store all three signal types.");
        }

        throw new InvalidOperationException(
            "Collector counters did not increase for: " + string.Join(", ", missing) + ".");
    }

    private async Task<bool> HasStoredTelemetryAsync(
        string path,
        DateTimeOffset fromAt,
        CancellationToken cancellationToken)
    {
        foreach (var signalType in new[]
        {
            "TELEMETRY_SIGNAL_TYPE_TRACE",
            "TELEMETRY_SIGNAL_TYPE_METRIC",
            "TELEMETRY_SIGNAL_TYPE_LOG",
        })
        {
            var query = $"?signalType={signalType}&pageSize=1"
                + $"&serviceName=asterloom.reference.client"
                + $"&fromAt={Uri.EscapeDataString(fromAt.ToString("O"))}";
            using var response = await platform.HttpClient.GetAsync(
                path + "/records" + query,
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.GetProperty("records").GetArrayLength() == 0)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string> TestRpcAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(settings.ReferenceBackendGrpcAddress);
        var client = new ReferenceAppService.ReferenceAppServiceClient(channel);
        var request = new RecordHeartbeatRequest
        {
            ClientInstanceId = settings.ServiceClientId + "-grpc",
            ClientVersion = "1.0.0",
            Platform = ReferenceAppProvisioner.GetRuntimeIdentifier(),
        };
        request.Attributes.Add("transport", "grpc");
        request.Attributes.Add("runId", state.RunId);
        _grpcHeartbeat = await client.RecordHeartbeatAsync(
            request,
            cancellationToken: cancellationToken);
        return $"Native gRPC wrote heartbeat {_grpcHeartbeat.Id}.";
    }

    private async Task<string> TestHttpTranscodingAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = settings.ReferenceBackendAddress };
        using var response = await client.PostAsJsonAsync(
            "api/reference/v1/heartbeats",
            new
            {
                clientInstanceId = settings.ServiceClientId + "-http",
                clientVersion = "1.0.0",
                platform = ReferenceAppProvisioner.GetRuntimeIdentifier(),
                attributes = new Dictionary<string, string>
                {
                    ["transport"] = "json-transcoding",
                    ["runId"] = state.RunId,
                },
            },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var status = await client.GetAsync("api/reference/v1/status", cancellationToken);
        await EnsureSuccessAsync(status, cancellationToken);
        return "Browser-compatible JSON Transcoding POST and GET both succeeded.";
    }

    private async Task<string> TestStorageAsync(CancellationToken cancellationToken)
    {
        using var client = new AsterloomStorageClient(
            platform.HttpClient,
            new AsterloomStorageClientOptions
            {
                Scope = new AsterloomStorageScope(state.TenantId),
                AllowInsecureTransferUrls = settings.AllowInsecureDevelopment,
            });
        var bytes = Encoding.UTF8.GetBytes(
            $"Asterloom reference storage probe {state.RunId} {DateTimeOffset.UtcNow:O}");
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageObject = await client.UploadAsync(
            new AsterloomStorageUploadRequest(
                state.StorageBucketId,
                $"diagnostics/{Guid.NewGuid():N}.txt",
                "diagnostic.txt",
                "application/octet-stream",
                bytes.LongLength,
                sha256,
                state.ApplicationId,
                state.EnvironmentId,
                new Dictionary<string, string> { ["runId"] = state.RunId }),
            new MemoryStream(bytes, writable: false),
            cancellationToken);
        using var downloaded = new MemoryStream();
        await client.DownloadToAsync(
            storageObject,
            downloaded,
            cancellationToken: cancellationToken);
        if (!bytes.AsSpan().SequenceEqual(downloaded.ToArray()))
        {
            throw new InvalidDataException("Downloaded storage content differs from the upload.");
        }

        return $"Uploaded and downloaded object {storageObject.Id} with SHA-256 verification.";
    }

    private async Task<string> TestPersistenceAsync(CancellationToken cancellationToken)
    {
        if (_grpcHeartbeat is null)
        {
            throw new InvalidOperationException("The RPC heartbeat did not run, so persistence cannot be verified.");
        }

        using var channel = GrpcChannel.ForAddress(settings.ReferenceBackendGrpcAddress);
        var client = new ReferenceAppService.ReferenceAppServiceClient(channel);
        var page = await client.ListHeartbeatsAsync(
            new ListHeartbeatsRequest { PageSize = 100 },
            cancellationToken: cancellationToken);
        if (!page.Heartbeats.Any(item => item.Id == _grpcHeartbeat.Id))
        {
            throw new InvalidOperationException("PostgreSQL did not return the recorded heartbeat.");
        }

        var status = await client.GetStatusAsync(
            new GetStatusRequest(),
            cancellationToken: cancellationToken);
        return $"PostgreSQL returned {status.HeartbeatCount} durable heartbeat(s).";
    }

    private async Task<string> TestOperationsAsync(CancellationToken cancellationToken)
    {
        var paths = new[]
        {
            "api/v1/operations/health",
            "api/v1/operations/apis?category=admin",
            "api/v1/operations/openapi",
            ScopePath() + "/flags?pageSize=1",
            ScopePath() + "/config/entries?pageSize=1",
            ScopePath() + "/release/channels?pageSize=1",
            ScopePath() + "/releases?pageSize=1&includeInactive=true",
            ScopePath() + "/analytics/schemas?pageSize=1",
            ScopePath() + "/telemetry/errors?pageSize=1",
            $"api/v1/tenants/{state.TenantId:D}/storage/buckets?pageSize=1",
        };
        foreach (var path in paths)
        {
            using var response = await platform.HttpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        return $"Operations/OpenAPI and {paths.Length - 3} formerly failing admin surfaces returned success.";
    }

    private async Task RunStepAsync(
        string capability,
        Func<CancellationToken, Task<string>> action,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = instrumentation.ActivitySource.StartActivity("reference.diagnostic.step");
        activity?.SetTag("asterloom.capability", capability);
        try
        {
            var detail = await action(cancellationToken);
            var duration = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _results.Add(new DiagnosticResult(capability, true, duration, detail, string.Empty, string.Empty));
            instrumentation.Diagnostics.Add(
                1,
                new KeyValuePair<string, object?>("capability", capability),
                new KeyValuePair<string, object?>("outcome", "success"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);
            var duration = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var (code, error) = Describe(exception);
            _results.Add(new DiagnosticResult(capability, false, duration, string.Empty, code, error));
            instrumentation.Diagnostics.Add(
                1,
                new KeyValuePair<string, object?>("capability", capability),
                new KeyValuePair<string, object?>("outcome", "failure"));
        }
    }

    private AsterloomAuthorizationScope Scope() => new(
        state.TenantId,
        state.ApplicationId,
        state.EnvironmentId);

    private string ScopePath() =>
        $"api/v1/tenants/{state.TenantId:D}/applications/{state.ApplicationId:D}"
        + $"/environments/{state.EnvironmentId:D}";

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new PlatformApiException(
                response.RequestMessage?.Method ?? HttpMethod.Get,
                response.RequestMessage?.RequestUri,
                response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    private static (string Code, string Error) Describe(Exception exception) => exception switch
    {
        RpcException rpc => ($"grpc.{rpc.StatusCode}", rpc.Status.Detail),
        PlatformApiException api => ($"http.{(int?)api.StatusCode}", api.Message),
        HttpRequestException http => ($"http.{(int?)http.StatusCode}", http.Message),
        _ => (exception.GetType().Name, exception.Message),
    };

    private static string Compact(string value)
    {
        var compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 180 ? compact : compact[..180] + "…";
    }

    private static async Task<IReadOnlyDictionary<string, double>> ReadCollectorSignalCountersAsync(
        HttpClient client,
        Uri address,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(address, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return CollectorSignalCounters.ToDictionary(
            counter => counter.Name,
            counter => ReadPrometheusCounter(payload, counter.Name, counter.Component),
            StringComparer.Ordinal);
    }

    private static double ReadPrometheusCounter(
        string payload,
        string counter,
        string component)
    {
        var value = 0d;
        foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line[0] == '#')
            {
                continue;
            }

            var nameEnd = line.IndexOfAny(['{', ' ']);
            if (nameEnd < 0)
            {
                continue;
            }

            var name = line[..nameEnd];
            if ((!string.Equals(name, counter, StringComparison.Ordinal)
                    && !string.Equals(name, counter + "_total", StringComparison.Ordinal))
                || !line.Contains(component, StringComparison.Ordinal))
            {
                continue;
            }

            var valueStart = line.LastIndexOf(' ');
            if (valueStart >= 0
                && double.TryParse(
                    line.AsSpan(valueStart + 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var sample))
            {
                value += sample;
            }
        }

        return value;
    }
}
