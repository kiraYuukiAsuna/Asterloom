using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Asterloom.ReferenceApp.Backend;

internal sealed class ReferenceAppInstrumentation : IDisposable
{
    public const string ActivitySourceName = "Asterloom.ReferenceApp.Backend";
    public const string MeterName = "Asterloom.ReferenceApp.Backend";

    public static ReferenceAppInstrumentation Instance { get; } = new();

    private ReferenceAppInstrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName);
        Meter = new Meter(MeterName);
        Heartbeats = Meter.CreateCounter<long>(
            "asterloom.reference.heartbeats",
            description: "Heartbeats accepted by the Asterloom reference backend.");
    }

    public ActivitySource ActivitySource { get; }

    public Meter Meter { get; }

    public Counter<long> Heartbeats { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
