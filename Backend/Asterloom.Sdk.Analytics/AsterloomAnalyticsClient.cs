using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Asterloom.Sdk.Analytics;

public sealed class AsterloomAnalyticsClient : IAsyncDisposable
{
    private const string IngestionPath = "api/v1/analytics/events:batch";
    private readonly HttpClient _httpClient;
    private readonly AsterloomAnalyticsClientOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Queue<AsterloomAnalyticsEvent> _queue = new();
    private readonly SemaphoreSlim _queueGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private int _disposeState;

    public AsterloomAnalyticsClient(
        HttpClient httpClient,
        AsterloomAnalyticsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(httpClient);
        _httpClient = httpClient;
        _options = options;
        _serializerOptions = new JsonSerializerOptions(options.SerializerOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        LoadOfflineQueue();
        _worker = RunWorkerAsync(_stopping.Token);
    }

    public event EventHandler<AsterloomAnalyticsDeliveryFailedEventArgs>? DeliveryFailed;

    public int QueuedCount
    {
        get
        {
            _queueGate.Wait();
            try
            {
                return _queue.Count;
            }
            finally
            {
                _queueGate.Release();
            }
        }
    }

    public async ValueTask<string> TrackAsync<TProperties>(
        string eventName,
        TProperties properties,
        AsterloomAnalyticsIdentity identity,
        IReadOnlyDictionary<string, object?>? context = null,
        DateTimeOffset? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        ArgumentNullException.ThrowIfNull(identity);
        var normalizedName = eventName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedName.Length is < 2 or > 100)
        {
            throw new ArgumentException("Event name must contain 2-100 characters.", nameof(eventName));
        }

        var mergedContext = new Dictionary<string, object?>(
            _options.CommonContext,
            StringComparer.Ordinal);
        if (context is not null)
        {
            foreach (var item in context)
            {
                mergedContext[item.Key] = item.Value;
            }
        }

        var timestamp = occurredAt ?? _options.TimeProvider.GetUtcNow();
        var analyticsEvent = new AsterloomAnalyticsEvent(
            Guid.CreateVersion7(timestamp).ToString("D"),
            normalizedName,
            timestamp,
            identity.ActorId?.Trim() ?? string.Empty,
            identity.AnonymousId?.Trim() ?? string.Empty,
            identity.SessionId?.Trim() ?? string.Empty,
            JsonSerializer.SerializeToElement(properties, _serializerOptions),
            JsonSerializer.SerializeToElement(mergedContext, _serializerOptions));

        var shouldFlush = false;
        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_queue.Count >= _options.QueueCapacity)
            {
                throw new AsterloomAnalyticsIngestionException(
                    "The analytics offline queue is full.");
            }

            await AppendOfflineAsync(analyticsEvent, cancellationToken).ConfigureAwait(false);
            _queue.Enqueue(analyticsEvent);
            shouldFlush = _queue.Count >= _options.BatchSize;
        }
        finally
        {
            _queueGate.Release();
        }

        if (shouldFlush)
        {
            _signal.Release();
        }

        return analyticsEvent.EventId;
    }

    public async Task<AsterloomAnalyticsFlushResult> FlushAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) == 2, this);
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accepted = 0;
            var rejected = 0;
            var deduplicated = 0;
            while (true)
            {
                var batch = await PeekBatchAsync(cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    return new(accepted, rejected, deduplicated, 0);
                }

                IngestionResponseDto response;
                try
                {
                    response = await SendWithRetryAsync(batch, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    return new(
                        accepted,
                        rejected,
                        deduplicated,
                        await GetQueueCountAsync(CancellationToken.None).ConfigureAwait(false));
                }

                await RemoveBatchAsync(batch.Count, cancellationToken).ConfigureAwait(false);
                accepted += response.Accepted;
                rejected += response.Rejected;
                deduplicated += response.Deduplicated;
                if (response.Failures.Count > 0)
                {
                    DeliveryFailed?.Invoke(
                        this,
                        new AsterloomAnalyticsDeliveryFailedEventArgs(
                            response.Failures.Select(static failure => new AsterloomAnalyticsFailure(
                                failure.EventId,
                                failure.ErrorCode,
                                failure.Message)).ToArray()));
                }
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        using (var shutdown = new CancellationTokenSource(_options.ShutdownTimeout))
        {
            try
            {
                await FlushAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
        }

        _stopping.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stopping.Dispose();
        _queueGate.Dispose();
        _flushGate.Dispose();
        _signal.Dispose();
        Volatile.Write(ref _disposeState, 2);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _ = await _signal.WaitAsync(_options.FlushInterval, cancellationToken)
                    .ConfigureAwait(false);
                while (_signal.Wait(0, cancellationToken))
                {
                }

                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<AsterloomAnalyticsEvent>> PeekBatchAsync(
        CancellationToken cancellationToken)
    {
        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _queue.Take(_options.BatchSize).ToArray();
        }
        finally
        {
            _queueGate.Release();
        }
    }

    private async Task RemoveBatchAsync(int count, CancellationToken cancellationToken)
    {
        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var index = 0; index < count; index++)
            {
                _queue.Dequeue();
            }

            await RewriteOfflineQueueAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _queueGate.Release();
        }
    }

    private async Task<int> GetQueueCountAsync(CancellationToken cancellationToken)
    {
        await _queueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _queue.Count;
        }
        finally
        {
            _queueGate.Release();
        }
    }

    private async Task<IngestionResponseDto> SendWithRetryAsync(
        IReadOnlyList<AsterloomAnalyticsEvent> batch,
        CancellationToken cancellationToken)
    {
        Exception? finalException = null;
        for (var attempt = 0; attempt <= _options.MaximumRetries; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.RequestTimeout);
                using var request = CreateRequest(batch);
                using var response = await _httpClient.SendAsync(request, timeout.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await JsonSerializer.DeserializeAsync<IngestionResponseDto>(
                        await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false),
                        _serializerOptions,
                        timeout.Token).ConfigureAwait(false)
                        ?? throw new AsterloomAnalyticsIngestionException(
                            "The analytics ingestion response was empty.");
                }

                if (!IsTransient(response.StatusCode))
                {
                    throw new AsterloomAnalyticsIngestionException(
                        $"Analytics ingestion failed with HTTP {(int)response.StatusCode}.");
                }

                finalException = new HttpRequestException(
                    $"Analytics ingestion returned transient HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
                if (attempt < _options.MaximumRetries)
                {
                    await DelayAsync(attempt, response.Headers.RetryAfter, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                finalException = exception;
                if (attempt < _options.MaximumRetries)
                {
                    await DelayAsync(attempt, null, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new AsterloomAnalyticsIngestionException(
            "Analytics ingestion remained unavailable after retries.",
            finalException ?? new HttpRequestException("Analytics ingestion failed."));
    }

    private HttpRequestMessage CreateRequest(IReadOnlyList<AsterloomAnalyticsEvent> batch)
    {
        var dto = new IngestionRequestDto(
            batch.Select(static item => new EventDto(
                item.EventId,
                item.EventName,
                item.OccurredAt,
                item.ActorId,
                item.AnonymousId,
                item.SessionId,
                item.Properties.GetRawText(),
                item.Context.GetRawText(),
                "asterloom-dotnet",
                typeof(AsterloomAnalyticsClient).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
                .ToArray());
        var payload = JsonSerializer.SerializeToUtf8Bytes(dto, _serializerOptions);
        HttpContent content;
        if (_options.CompressionThresholdBytes > 0
            && payload.Length >= _options.CompressionThresholdBytes)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(payload);
            }

            content = new ByteArrayContent(output.ToArray());
            content.Headers.ContentEncoding.Add("gzip");
        }
        else
        {
            content = new ByteArrayContent(payload);
        }

        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, ResolveUri()) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Asterloom-Write-Key", _options.WriteKey);
        return request;
    }

    private Uri ResolveUri()
    {
        var baseAddress = _options.BaseAddress ?? _httpClient.BaseAddress!;
        return new Uri(baseAddress, IngestionPath);
    }

    private async Task DelayAsync(
        int attempt,
        RetryConditionHeaderValue? retryAfter,
        CancellationToken cancellationToken)
    {
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date
                ? date - _options.TimeProvider.GetUtcNow()
                : TimeSpan.FromMilliseconds(Math.Min(5_000, 200 * Math.Pow(2, attempt))));
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, _options.TimeProvider, cancellationToken).ConfigureAwait(false);
    }

    private void LoadOfflineQueue()
    {
        var path = _options.OfflineQueuePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path).Take(_options.QueueCapacity))
        {
            try
            {
                var item = JsonSerializer.Deserialize<AsterloomAnalyticsEvent>(
                    line,
                    _serializerOptions);
                if (item is not null)
                {
                    _queue.Enqueue(item);
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private async Task AppendOfflineAsync(
        AsterloomAnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken)
    {
        var path = _options.OfflineQueuePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsureQueueDirectory(path);
        var line = JsonSerializer.Serialize(analyticsEvent, _serializerOptions) + Environment.NewLine;
        await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RewriteOfflineQueueAsync(CancellationToken cancellationToken)
    {
        var path = _options.OfflineQueuePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        EnsureQueueDirectory(path);
        var lines = _queue.Select(item => JsonSerializer.Serialize(item, _serializerOptions));
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureQueueDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private sealed record IngestionRequestDto(IReadOnlyList<EventDto> Events);

    private sealed record EventDto(
        string EventId,
        string EventName,
        DateTimeOffset OccurredAt,
        string ActorId,
        string AnonymousId,
        string SessionId,
        string PropertiesJson,
        string ContextJson,
        string SdkName,
        string SdkVersion);

    private sealed record IngestionResponseDto(
        int Accepted,
        int Rejected,
        int Deduplicated,
        IReadOnlyList<FailureDto> Failures)
    {
        public IReadOnlyList<FailureDto> Failures { get; init; } = Failures ?? [];
    }

    private sealed record FailureDto(string EventId, string ErrorCode, string Message);
}
