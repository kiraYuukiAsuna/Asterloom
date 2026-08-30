using Asterloom.Modules.Analytics.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Analytics;

internal sealed class AnalyticsRetentionWorker(
    IAnalyticsStore store,
    TimeProvider timeProvider,
    ILogger<AnalyticsRetentionWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> LogPurged =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(7101, nameof(LogPurged)),
            "Purged {EventCount} expired analytics events.");
    private static readonly Action<ILogger, Exception?> LogFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(7102, nameof(LogFailure)),
            "Analytics retention cleanup failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var count = await store.PurgeExpiredEventsAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken);
                if (count > 0)
                {
                    LogPurged(logger, count, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
            }
        }
    }
}
