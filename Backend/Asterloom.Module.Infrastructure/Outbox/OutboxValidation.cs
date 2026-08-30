using System.Text.Json;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Infrastructure.Outbox;

internal static class OutboxValidation
{
    private const int MaximumPayloadBytes = 1024 * 1024;

    public static void ValidateDraft(OutboxMessageDraft message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Id == Guid.Empty)
        {
            throw new ArgumentException("An outbox event ID is required.", nameof(message));
        }

        ValidateName(message.EventType, nameof(message.EventType), 300);
        if (message.SchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message),
                "The outbox schema version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(message.Payload)
            || System.Text.Encoding.UTF8.GetByteCount(message.Payload) > MaximumPayloadBytes)
        {
            throw new ArgumentException(
                $"The outbox payload must be valid JSON no larger than {MaximumPayloadBytes} bytes.",
                nameof(message));
        }

        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            if (document.RootElement.ValueKind is JsonValueKind.Undefined)
            {
                throw new JsonException("The JSON payload is undefined.");
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The outbox payload must be valid JSON.", nameof(message), exception);
        }

        ValidateName(message.CorrelationId, nameof(message.CorrelationId), 200);
        if (message.EnvironmentId is not null && message.ApplicationId is null
            || message.ApplicationId is not null && message.TenantId is null)
        {
            throw new ArgumentException("The outbox scope must be nested.", nameof(message));
        }
    }

    public static string ValidateWorkerId(string workerId)
    {
        ValidateName(workerId, nameof(workerId), 200);
        return workerId;
    }

    public static string ValidateConsumerName(string consumerName)
    {
        ValidateName(consumerName, nameof(consumerName), 200);
        return consumerName;
    }

    public static string[] ValidateEventTypes(IReadOnlyCollection<string> eventTypes)
    {
        ArgumentNullException.ThrowIfNull(eventTypes);
        if (eventTypes.Count is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventTypes),
                "Between 1 and 500 handled event types are required.");
        }

        var normalized = eventTypes.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var eventType in normalized)
        {
            ValidateName(eventType, nameof(eventTypes), 300);
        }

        return normalized;
    }

    public static void ValidateClaim(TimeSpan leaseDuration, int maximumCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            leaseDuration,
            TimeSpan.Zero);

        if (maximumCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }
    }

    private static void ValidateName(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"A value between 1 and {maximumLength} characters is required.",
                parameterName);
        }
    }
}
