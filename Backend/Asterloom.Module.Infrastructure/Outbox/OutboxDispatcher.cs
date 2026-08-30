using Asterloom.Modules.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Infrastructure.Outbox;

internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    OutboxDispatcherOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> LogDispatcherFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2210, nameof(LogDispatcherFailure)),
            "Outbox dispatcher cycle failed: {ErrorCode}.");

    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                processed = await processor.ProcessBatchAsync(_workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatcherFailure(logger, exception.GetType().Name, null);
            }

            if (processed == 0)
            {
                await Task.Delay(options.PollInterval, timeProvider, stoppingToken);
            }
        }
    }
}
