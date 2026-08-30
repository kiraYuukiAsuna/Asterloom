using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Asterloom.ReferenceApp.Client;

internal sealed class ReferenceClientInstrumentation : IDisposable
{
    public const string ActivitySourceName = "Asterloom.ReferenceApp.Client";
    public const string MeterName = "Asterloom.ReferenceApp.Client";

    public ReferenceClientInstrumentation()
    {
        ActivitySource = new ActivitySource(ActivitySourceName);
        Meter = new Meter(MeterName);
        Diagnostics = Meter.CreateCounter<long>(
            "asterloom.reference.diagnostics",
            description: "Reference diagnostic steps completed by capability and outcome.");
    }

    public ActivitySource ActivitySource { get; }

    public Meter Meter { get; }

    public Counter<long> Diagnostics { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        Meter.Dispose();
    }
}
