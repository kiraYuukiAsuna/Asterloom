using Microsoft.Extensions.Configuration;

namespace Asterloom.Modules.Outbox;

public sealed record OutboxDispatcherOptions(
    bool Enabled,
    int BatchSize,
    int MaximumAttempts,
    TimeSpan PollInterval,
    TimeSpan LeaseDuration,
    TimeSpan BaseRetryDelay,
    TimeSpan MaximumRetryDelay)
{
    public static OutboxDispatcherOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Outbox");
        var options = new OutboxDispatcherOptions(
            section.GetValue("Enabled", true),
            section.GetValue("BatchSize", 50),
            section.GetValue("MaximumAttempts", 10),
            TimeSpan.FromMilliseconds(section.GetValue("PollIntervalMilliseconds", 1000)),
            TimeSpan.FromSeconds(section.GetValue("LeaseSeconds", 30)),
            TimeSpan.FromSeconds(section.GetValue("BaseRetrySeconds", 2)),
            TimeSpan.FromSeconds(section.GetValue("MaximumRetrySeconds", 300)));
        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (BatchSize is < 1 or > 500)
        {
            throw new InvalidOperationException("Outbox BatchSize must be between 1 and 500.");
        }

        if (MaximumAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Outbox MaximumAttempts must be between 1 and 100.");
        }

        if (PollInterval < TimeSpan.FromMilliseconds(100)
            || PollInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                "Outbox PollIntervalMilliseconds must be between 100 and 300000.");
        }

        if (LeaseDuration < TimeSpan.FromSeconds(5)
            || LeaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                "Outbox LeaseSeconds must be between 5 and 1800.");
        }

        if (BaseRetryDelay < TimeSpan.FromMilliseconds(100)
            || BaseRetryDelay > MaximumRetryDelay
            || MaximumRetryDelay > TimeSpan.FromHours(24))
        {
            throw new InvalidOperationException("Outbox retry delays are invalid.");
        }
    }
}
