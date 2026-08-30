using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Asterloom.Modules.Analytics.Model;
using Asterloom.Modules.Analytics.Persistence;
using Asterloom.Modules.Errors;

namespace Asterloom.Modules.Analytics;

public sealed class AnalyticsIngestionService(IAnalyticsStore store, TimeProvider timeProvider)
{
    private const int MaximumBatchSize = 100;
    private static readonly Meter Meter = new("Asterloom.Analytics", "1.0.0");
    private static readonly Counter<long> AcceptedCounter =
        Meter.CreateCounter<long>("asterloom.analytics.events.accepted");
    private static readonly Counter<long> RejectedCounter =
        Meter.CreateCounter<long>("asterloom.analytics.events.rejected");
    private static readonly Counter<long> DeduplicatedCounter =
        Meter.CreateCounter<long>("asterloom.analytics.events.deduplicated");
    private static readonly Regex EventNamePattern = new(
        "^[a-z][a-z0-9]([a-z0-9._-]{0,98}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<AnalyticsIngestionResult> IngestAsync(
        string? suppliedSecret,
        IReadOnlyList<AnalyticsEventEnvelope> events,
        CancellationToken cancellationToken)
    {
        var writeKey = await AuthenticateAsync(suppliedSecret, cancellationToken);
        if (events.Count is < 1 or > MaximumBatchSize)
        {
            throw Invalid("events", $"An ingestion batch must contain 1-{MaximumBatchSize} events.");
        }

        var accepted = 0;
        var deduplicated = 0;
        var failures = new List<AnalyticsIngestionFailure>();
        foreach (var input in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var storedEvent = await ValidateAsync(writeKey, input, cancellationToken);
                var outcome = await store.AppendEventAsync(storedEvent, cancellationToken);
                if (outcome == AnalyticsAppendOutcome.Deduplicated)
                {
                    deduplicated++;
                }
                else
                {
                    accepted++;
                }
            }
            catch (AsterloomException exception) when (
                exception.Kind is AsterloomErrorKind.InvalidArgument
                    or AsterloomErrorKind.NotFound
                    or AsterloomErrorKind.FailedPrecondition)
            {
                failures.Add(new(
                    input.EventId,
                    exception.ErrorCode,
                    exception.FieldErrors.Values.SelectMany(static item => item).FirstOrDefault()
                        ?? exception.Message));
            }
        }

        if (accepted > 0)
        {
            AcceptedCounter.Add(accepted);
        }

        if (deduplicated > 0)
        {
            DeduplicatedCounter.Add(deduplicated);
        }

        if (failures.Count > 0)
        {
            RejectedCounter.Add(failures.Count);
        }

        await store.TouchWriteKeyAsync(
            writeKey.Id,
            timeProvider.GetUtcNow(),
            CancellationToken.None);

        return new(accepted, failures.Count, deduplicated, failures);
    }

    private async Task<AnalyticsWriteKey> AuthenticateAsync(
        string? suppliedSecret,
        CancellationToken cancellationToken)
    {
        var secret = suppliedSecret?.Trim() ?? string.Empty;
        var prefix = AnalyticsManagementService.ExtractPrefix(secret);
        if (prefix is null)
        {
            throw Unauthenticated();
        }

        var writeKey = await store.GetWriteKeyByPrefixAsync(prefix, cancellationToken);
        if (writeKey is null
            || writeKey.Status != AnalyticsWriteKeyStatus.Active
            || !CryptographicOperations.FixedTimeEquals(
                writeKey.SecretHash,
                AnalyticsManagementService.HashSecret(secret)))
        {
            throw Unauthenticated();
        }

        return writeKey;
    }

    private async Task<StoredAnalyticsEvent> ValidateAsync(
        AnalyticsWriteKey writeKey,
        AnalyticsEventEnvelope input,
        CancellationToken cancellationToken)
    {
        var eventId = AnalyticsManagementService.RequireText(input.EventId, "eventId", 128);
        if (eventId.Any(char.IsControl))
        {
            throw Invalid("eventId", "Event ID must not contain control characters.");
        }

        var eventName = input.EventName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!EventNamePattern.IsMatch(eventName))
        {
            throw Invalid("eventName", "Event name has an invalid format.");
        }

        var schema = await store.GetActiveEventSchemaByKeyAsync(
            writeKey.Scope,
            eventName,
            cancellationToken)
            ?? throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "analytics_schema_unavailable",
                "No active event schema exists for this event name.");
        var now = timeProvider.GetUtcNow();
        var occurredAt = input.OccurredAt ?? now;
        if (occurredAt > now.AddHours(24)
            || occurredAt < now.AddDays(-schema.RetentionDays))
        {
            throw Invalid(
                "occurredAt",
                "Event time is outside the accepted retention or future-time window.");
        }

        var actorId = AnalyticsManagementService.NormalizeText(input.ActorId, "actorId", 200);
        var anonymousId = AnalyticsManagementService.NormalizeText(
            input.AnonymousId,
            "anonymousId",
            200);
        if (actorId.Length == 0 && anonymousId.Length == 0)
        {
            throw Invalid("actorId", "Either actorId or anonymousId is required.");
        }

        return new StoredAnalyticsEvent(
            Guid.CreateVersion7(now),
            eventId,
            writeKey.Scope,
            schema.Id,
            eventName,
            schema.Version,
            occurredAt,
            now,
            actorId,
            anonymousId,
            AnalyticsManagementService.NormalizeText(input.SessionId, "sessionId", 200),
            AnalyticsSchemaValidator.ValidateAndRedactProperties(schema, input.PropertiesJson),
            AnalyticsSchemaValidator.ValidateAndNormalizeContext(input.ContextJson),
            AnalyticsManagementService.NormalizeText(input.SdkName, "sdkName", 100),
            AnalyticsManagementService.NormalizeText(input.SdkVersion, "sdkVersion", 100),
            writeKey.Prefix);
    }

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "analytics_event_invalid",
        "The analytics event is invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });

    private static AsterloomException Unauthenticated() => new(
        AsterloomErrorKind.Unauthenticated,
        "analytics_write_key_invalid",
        "A valid active analytics write key is required.");
}

public sealed record AnalyticsEventEnvelope(
    string EventId,
    string EventName,
    DateTimeOffset? OccurredAt,
    string ActorId,
    string AnonymousId,
    string SessionId,
    string PropertiesJson,
    string ContextJson,
    string SdkName,
    string SdkVersion);

public sealed record AnalyticsIngestionFailure(string EventId, string ErrorCode, string Message);

public sealed record AnalyticsIngestionResult(
    int Accepted,
    int Rejected,
    int Deduplicated,
    IReadOnlyList<AnalyticsIngestionFailure> Failures);
