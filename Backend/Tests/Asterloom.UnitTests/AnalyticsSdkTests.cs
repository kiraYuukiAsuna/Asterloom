using System.Net;
using System.Net.Http.Json;
using Asterloom.Sdk.Analytics;

namespace Asterloom.UnitTests;

public sealed class AnalyticsSdkTests
{
    [Fact]
    public async Task TrackWaitsForBatchThresholdOrExplicitFlush()
    {
        var handler = new RecordingIngestionHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://asterloom.test/"),
        };
        await using var client = new AsterloomAnalyticsClient(
            httpClient,
            new AsterloomAnalyticsClientOptions
            {
                WriteKey = "ast_an_unit_test",
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMinutes(1),
                MaximumRetries = 0,
            });

        await client.TrackAsync(
            "checkout.completed",
            new { orderId = "order-1" },
            new AsterloomAnalyticsIdentity(ActorId: "user-1"));

        var prematureSend = await Task.WhenAny(
            handler.RequestStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        Assert.NotSame(handler.RequestStarted.Task, prematureSend);
        Assert.Equal(1, client.QueuedCount);

        var result = await client.FlushAsync();

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Remaining);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class RecordingIngestionHandler : HttpMessageHandler
    {
        private int _requestCount;

        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            RequestStarted.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    accepted = 1,
                    rejected = 0,
                    deduplicated = 0,
                    failures = Array.Empty<object>(),
                }),
            });
        }
    }
}
