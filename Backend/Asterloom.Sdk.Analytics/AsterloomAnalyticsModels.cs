using System.Text.Json;

namespace Asterloom.Sdk.Analytics;

public sealed record AsterloomAnalyticsIdentity(
    string ActorId = "",
    string AnonymousId = "",
    string SessionId = "");

public sealed record AsterloomAnalyticsEvent(
    string EventId,
    string EventName,
    DateTimeOffset OccurredAt,
    string ActorId,
    string AnonymousId,
    string SessionId,
    JsonElement Properties,
    JsonElement Context);

public sealed record AsterloomAnalyticsFlushResult(
    int Accepted,
    int Rejected,
    int Deduplicated,
    int Remaining);

public sealed record AsterloomAnalyticsFailure(
    string EventId,
    string ErrorCode,
    string Message);

public sealed class AsterloomAnalyticsDeliveryFailedEventArgs(
    IReadOnlyList<AsterloomAnalyticsFailure> failures) : EventArgs
{
    public IReadOnlyList<AsterloomAnalyticsFailure> Failures { get; } = failures;
}

public sealed class AsterloomAnalyticsIngestionException : Exception
{
    public AsterloomAnalyticsIngestionException(string message)
        : base(message)
    {
    }

    public AsterloomAnalyticsIngestionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
