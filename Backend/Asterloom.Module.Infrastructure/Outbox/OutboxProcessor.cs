using Asterloom.Modules.Outbox;
using Microsoft.Extensions.Logging;

namespace Asterloom.Modules.Infrastructure.Outbox;

public sealed class OutboxProcessor
{
    private static readonly Action<ILogger, Guid, string, int, string, Exception?>
        LogDeliveryFailure = LoggerMessage.Define<Guid, string, int, string>(
            LogLevel.Warning,
            new EventId(2201, nameof(LogDeliveryFailure)),
            "Outbox event {EventId} ({EventType}) failed on attempt {AttemptCount}: {ErrorCode}.");

    private static readonly Action<ILogger, Guid, string, Exception?> LogLeaseLost =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2202, nameof(LogLeaseLost)),
            "Outbox lease was lost for event {EventId} while {Action}.");

    private readonly Dictionary<string, IOutboxMessageConsumer[]> _handlers;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxDispatcherOptions _options;
    private readonly IOutboxStore _store;
    private readonly TimeProvider _timeProvider;

    public OutboxProcessor(
        IOutboxStore store,
        IEnumerable<IOutboxMessageConsumer> handlers,
        OutboxDispatcherOptions options,
        TimeProvider timeProvider,
        ILogger<OutboxProcessor> logger)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _handlers = ValidateHandlers(handlers);
    }

    public async Task<int> ProcessBatchAsync(
        string workerId,
        CancellationToken cancellationToken)
    {
        if (_handlers.Count == 0)
        {
            return 0;
        }

        var messages = await _store.ClaimPendingAsync(
            workerId,
            _handlers.Keys,
            _timeProvider.GetUtcNow(),
            _options.LeaseDuration,
            _options.BatchSize,
            cancellationToken);
        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, workerId, cancellationToken);
        }

        return messages.Count;
    }

    private async Task ProcessMessageAsync(
        OutboxMessage message,
        string workerId,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var handler in _handlers[message.EventType])
            {
                if (await _store.HasReceiptAsync(
                        message.Id,
                        handler.ConsumerName,
                        cancellationToken))
                {
                    continue;
                }

                await handler.HandleAsync(message, cancellationToken);
                var receiptRecorded = await _store.RecordReceiptAsync(
                    message.Id,
                    handler.ConsumerName,
                    workerId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
                if (!receiptRecorded)
                {
                    throw new InvalidOperationException("outbox_lease_lost");
                }
            }

            if (!await _store.MarkProcessedAsync(
                    message.Id,
                    workerId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken))
            {
                LogLeaseLost(_logger, message.Id, "marking it processed", null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var now = _timeProvider.GetUtcNow();
            var deadLettered = message.AttemptCount >= _options.MaximumAttempts;
            var errorCode = exception is InvalidOperationException
                && string.Equals(
                    exception.Message,
                    "outbox_lease_lost",
                    StringComparison.Ordinal)
                ? "outbox_lease_lost"
                : exception.GetType().Name;
            LogDeliveryFailure(
                _logger,
                message.Id,
                message.EventType,
                message.AttemptCount,
                errorCode,
                null);
            var updated = await _store.MarkFailedAsync(
                message.Id,
                workerId,
                now.Add(CalculateRetryDelay(message.AttemptCount)),
                errorCode,
                deadLettered ? now : null,
                cancellationToken);
            if (!updated)
            {
                LogLeaseLost(_logger, message.Id, "recording its failure", null);
            }
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponent = Math.Min(Math.Max(0, attemptCount - 1), 30);
        var multiplier = Math.Pow(2, exponent);
        var milliseconds = Math.Min(
            _options.MaximumRetryDelay.TotalMilliseconds,
            _options.BaseRetryDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static Dictionary<string, IOutboxMessageConsumer[]> ValidateHandlers(
        IEnumerable<IOutboxMessageConsumer> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var items = handlers.ToArray();
        foreach (var handler in items)
        {
            ArgumentNullException.ThrowIfNull(handler);
            OutboxValidation.ValidateEventTypes([handler.EventType]);
            OutboxValidation.ValidateConsumerName(handler.ConsumerName);
        }

        var duplicate = items
            .GroupBy(
                static handler => (handler.EventType, handler.ConsumerName),
                EqualityComparer<(string EventType, string ConsumerName)>.Default)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Outbox handler '{duplicate.Key.ConsumerName}' is registered more than once " +
                $"for '{duplicate.Key.EventType}'.");
        }

        return items
            .GroupBy(static handler => handler.EventType, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(
                    static handler => handler.ConsumerName,
                    StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }
}
