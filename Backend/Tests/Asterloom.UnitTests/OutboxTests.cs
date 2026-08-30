using Asterloom.Modules.Hosting;
using Asterloom.Modules.Infrastructure;
using Asterloom.Modules.Infrastructure.Outbox;
using Asterloom.Modules.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Asterloom.UnitTests;

public sealed class OutboxTests
{
    [Fact]
    public async Task DispatcherProcessesHandledEventsOnceAndLeavesUnhandledEventsPending()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero));
        var consumer = new RecordingConsumer("test-consumer");
        await using var provider = CreateProvider(clock, [consumer]);
        var store = provider.GetRequiredService<IOutboxStore>();
        var handled = CreateMessage("asterloom.test.handled.v1", clock.GetUtcNow());
        var unhandled = CreateMessage("asterloom.test.unhandled.v1", clock.GetUtcNow());
        await store.EnqueueAsync(handled, CancellationToken.None);
        await store.EnqueueAsync(unhandled, CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        var firstCount = await processor.ProcessBatchAsync("worker-1", CancellationToken.None);
        var secondCount = await processor.ProcessBatchAsync("worker-1", CancellationToken.None);
        var handledState = await store.GetAsync(handled.Id, CancellationToken.None);
        var unhandledState = await store.GetAsync(unhandled.Id, CancellationToken.None);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
        Assert.Equal(1, consumer.CallCount);
        Assert.NotNull(handledState!.ProcessedAt);
        Assert.Equal(1, handledState.AttemptCount);
        Assert.Null(unhandledState!.ProcessedAt);
        Assert.Equal(0, unhandledState.AttemptCount);
    }

    [Fact]
    public async Task ReceiptsSkipCompletedConsumersWhenAnotherConsumerRetries()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 11, 0, 0, TimeSpan.Zero));
        var first = new RecordingConsumer("first-consumer");
        var second = new RecordingConsumer("second-consumer", failuresBeforeSuccess: 1);
        await using var provider = CreateProvider(clock, [first, second]);
        var store = provider.GetRequiredService<IOutboxStore>();
        var message = CreateMessage("asterloom.test.handled.v1", clock.GetUtcNow());
        await store.EnqueueAsync(message, CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        Assert.Equal(
            1,
            await processor.ProcessBatchAsync("worker-retry", CancellationToken.None));
        var failedState = await store.GetAsync(message.Id, CancellationToken.None);
        Assert.Null(failedState!.ProcessedAt);
        Assert.Equal(1, failedState.AttemptCount);
        Assert.Equal("InvalidOperationException", failedState.LastError);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(
            1,
            await processor.ProcessBatchAsync("worker-retry", CancellationToken.None));
        var completedState = await store.GetAsync(message.Id, CancellationToken.None);

        Assert.Equal(1, first.CallCount);
        Assert.Equal(2, second.CallCount);
        Assert.NotNull(completedState!.ProcessedAt);
        Assert.Equal(2, completedState.AttemptCount);
        Assert.Equal(string.Empty, completedState.LastError);
    }

    [Fact]
    public async Task DispatcherDeadLettersAfterConfiguredMaximumAttempts()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var consumer = new RecordingConsumer("failing-consumer", failuresBeforeSuccess: 10);
        await using var provider = CreateProvider(clock, [consumer], maximumAttempts: 2);
        var store = provider.GetRequiredService<IOutboxStore>();
        var message = CreateMessage("asterloom.test.handled.v1", clock.GetUtcNow());
        await store.EnqueueAsync(message, CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        await processor.ProcessBatchAsync("worker-dead", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await processor.ProcessBatchAsync("worker-dead", CancellationToken.None);
        var dead = await store.GetAsync(message.Id, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.NotNull(dead!.DeadLetteredAt);
        Assert.Null(dead.ProcessedAt);
        Assert.Equal(2, dead.AttemptCount);
        Assert.Equal(0, await processor.ProcessBatchAsync("worker-dead", CancellationToken.None));
    }

    private static ServiceProvider CreateProvider(
        MutableTimeProvider clock,
        IReadOnlyCollection<IOutboxMessageConsumer> consumers,
        int maximumAttempts = 10)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "Memory",
                    ["Outbox:Enabled"] = "false",
                    ["Outbox:MaximumAttempts"] = maximumAttempts.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        foreach (var consumer in consumers)
        {
            services.AddSingleton<IOutboxMessageConsumer>(consumer);
        }

        services.AddAsterloomModules(configuration, new InfrastructureModule());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static OutboxMessageDraft CreateMessage(
        string eventType,
        DateTimeOffset occurredAt) =>
        OutboxMessageFactory.Create(
            eventType,
            schemaVersion: 1,
            new { resourceId = Guid.CreateVersion7() },
            correlationId: Guid.NewGuid().ToString("N"),
            occurredAt);

    private sealed class RecordingConsumer(
        string consumerName,
        int failuresBeforeSuccess = 0) : IOutboxMessageConsumer
    {
        public string EventType => "asterloom.test.handled.v1";

        public string ConsumerName { get; } = consumerName;

        public int CallCount { get; private set; }

        public Task HandleAsync(
            OutboxMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (CallCount <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Expected test failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
