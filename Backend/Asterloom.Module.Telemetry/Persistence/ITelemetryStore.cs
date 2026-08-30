using Asterloom.Modules.Telemetry.Model;

namespace Asterloom.Modules.Telemetry.Persistence;

public interface ITelemetryStore
{
    Task<TelemetryStorePage<TelemetrySource>> ListSourcesAsync(
        TelemetryScope scope,
        TelemetryPageRequest request,
        CancellationToken cancellationToken);

    Task<TelemetrySource?> GetSourceAsync(
        TelemetryScope scope,
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateSourceAsync(
        TelemetrySource source,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateSourceAsync(
        TelemetrySource source,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<TelemetrySettings?> GetSettingsAsync(
        TelemetryScope scope,
        CancellationToken cancellationToken);

    Task<bool> TryUpsertSettingsAsync(
        TelemetrySettings settings,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task AppendErrorAsync(
        TelemetryError telemetryError,
        CancellationToken cancellationToken);

    Task<TelemetryStorePage<TelemetryError>> ListErrorsAsync(
        TelemetryScope scope,
        TelemetryErrorFilter filter,
        CancellationToken cancellationToken);
}
