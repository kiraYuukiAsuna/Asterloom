using System.Text.Json;

namespace Asterloom.Sdk.Analytics;

public sealed class AsterloomAnalyticsClientOptions
{
    public Uri? BaseAddress { get; init; }

    public required string WriteKey { get; init; }

    public int BatchSize { get; init; } = 20;

    public int QueueCapacity { get; init; } = 10_000;

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaximumRetries { get; init; } = 3;

    public int CompressionThresholdBytes { get; init; } = 1_024;

    public string? OfflineQueuePath { get; init; }

    public IReadOnlyDictionary<string, object?> CommonContext { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    public JsonSerializerOptions SerializerOptions { get; init; } =
        new(JsonSerializerDefaults.Web);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal void Validate(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (BaseAddress is null && httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "Either options.BaseAddress or HttpClient.BaseAddress must be configured.");
        }

        if (string.IsNullOrWhiteSpace(WriteKey) || WriteKey.Length > 256)
        {
            throw new InvalidOperationException("A valid analytics write key is required.");
        }

        if (BatchSize is < 1 or > 100)
        {
            throw new InvalidOperationException("Batch size must be 1-100.");
        }

        if (QueueCapacity < BatchSize || QueueCapacity > 1_000_000)
        {
            throw new InvalidOperationException(
                "Queue capacity must be at least the batch size and no more than 1,000,000.");
        }

        if (FlushInterval <= TimeSpan.Zero || FlushInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException("Flush interval is outside the supported range.");
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException("Request timeout is outside the supported range.");
        }

        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("Shutdown timeout is outside the supported range.");
        }

        if (MaximumRetries is < 0 or > 10)
        {
            throw new InvalidOperationException("Maximum retries must be 0-10.");
        }

        if (CompressionThresholdBytes is < 0 or > 4 * 1024 * 1024)
        {
            throw new InvalidOperationException("Compression threshold is outside the supported range.");
        }

        if (OfflineQueuePath?.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException("Offline queue path is invalid.");
        }
    }
}
