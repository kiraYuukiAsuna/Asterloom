using System.Diagnostics;
using Asterloom.Modules.Telemetry.Model;

namespace Asterloom.Modules.Telemetry;

public sealed class TelemetryCollectorHealthProbe(
    HttpClient httpClient,
    TelemetryManagementOptions options,
    TimeProvider timeProvider)
{
    public async Task<TelemetryCollectorHealth> CheckAsync(
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await httpClient.GetAsync(
                options.CollectorHealthEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var latency = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return new(
                response.IsSuccessStatusCode
                    ? TelemetryCollectorStatus.Healthy
                    : TelemetryCollectorStatus.Degraded,
                options.CollectorHealthEndpoint.ToString(),
                timeProvider.GetUtcNow(),
                latency,
                response.IsSuccessStatusCode
                    ? "The OpenTelemetry Collector health endpoint is available."
                    : $"The Collector returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(started, "The Collector health check timed out.");
        }
        catch (HttpRequestException)
        {
            return Unavailable(started, "The Collector health endpoint is unavailable.");
        }
    }

    private TelemetryCollectorHealth Unavailable(long started, string message) => new(
        TelemetryCollectorStatus.Unavailable,
        options.CollectorHealthEndpoint.ToString(),
        timeProvider.GetUtcNow(),
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        message);
}
