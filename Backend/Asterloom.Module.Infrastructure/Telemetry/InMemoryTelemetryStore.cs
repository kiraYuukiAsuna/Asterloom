using Asterloom.Modules.Telemetry.Model;
using Asterloom.Modules.Telemetry.Persistence;

namespace Asterloom.Modules.Infrastructure.Telemetry;

internal sealed class InMemoryTelemetryStore : ITelemetryStore
{
    private const int MaximumErrors = 10_000;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, TelemetrySource> _sources = [];
    private readonly Dictionary<TelemetryScope, TelemetrySettings> _settings = [];
    private readonly Dictionary<Guid, TelemetryError> _errors = [];

    public Task<TelemetryStorePage<TelemetrySource>> ListSourcesAsync(
        TelemetryScope scope,
        TelemetryPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _sources.Values
                    .Where(item => item.Scope == scope)
                    .Where(item => request.IncludeArchived
                        || item.Status == TelemetryResourceStatus.Active)
                    .Where(item => Matches(
                        request.Query,
                        item.Key,
                        item.DisplayName,
                        item.Description,
                        item.ServiceName))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.Id),
                request.Offset,
                request.PageSize));
        }
    }

    public Task<TelemetrySource?> GetSourceAsync(
        TelemetryScope scope,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _sources.TryGetValue(sourceId, out var source) && source.Scope == scope
                    ? source
                    : null);
        }
    }

    public Task<bool> TryCreateSourceAsync(
        TelemetrySource source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sources.ContainsKey(source.Id)
                || _sources.Values.Any(item => item.Scope == source.Scope
                    && (string.Equals(item.Key, source.Key, StringComparison.Ordinal)
                        || string.Equals(item.ServiceName, source.ServiceName, StringComparison.Ordinal))))
            {
                return Task.FromResult(false);
            }

            _sources.Add(source.Id, source);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateSourceAsync(
        TelemetrySource source,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sources.TryGetValue(source.Id, out var current)
                || current.Scope != source.Scope
                || current.Version != expectedVersion
                || _sources.Values.Any(item => item.Id != source.Id
                    && item.Scope == source.Scope
                    && string.Equals(item.ServiceName, source.ServiceName, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            _sources[source.Id] = source;
            return Task.FromResult(true);
        }
    }

    public Task<TelemetrySettings?> GetSettingsAsync(
        TelemetryScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_settings.GetValueOrDefault(scope));
        }
    }

    public Task<bool> TryUpsertSettingsAsync(
        TelemetrySettings settings,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_settings.TryGetValue(settings.Scope, out var current))
            {
                if (current.Version != expectedVersion)
                {
                    return Task.FromResult(false);
                }
            }
            else if (expectedVersion != 0)
            {
                return Task.FromResult(false);
            }

            _settings[settings.Scope] = settings;
            return Task.FromResult(true);
        }
    }

    public Task AppendErrorAsync(
        TelemetryError telemetryError,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _errors[telemetryError.Id] = telemetryError;
            if (_errors.Count > MaximumErrors)
            {
                foreach (var id in _errors.Values
                    .OrderBy(static item => item.OccurredAt)
                    .Take(_errors.Count - MaximumErrors)
                    .Select(static item => item.Id)
                    .ToArray())
                {
                    _errors.Remove(id);
                }
            }

            return Task.CompletedTask;
        }
    }

    public Task<TelemetryStorePage<TelemetryError>> ListErrorsAsync(
        TelemetryScope scope,
        TelemetryErrorFilter filter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(ToPage(
                _errors.Values
                    .Where(item => item.Scope == scope)
                    .Where(item => filter.ServiceName.Length == 0
                        || string.Equals(item.ServiceName, filter.ServiceName, StringComparison.Ordinal))
                    .Where(item => filter.TraceId.Length == 0
                        || string.Equals(item.TraceId, filter.TraceId, StringComparison.Ordinal))
                    .OrderByDescending(static item => item.OccurredAt)
                    .ThenByDescending(static item => item.Id),
                filter.Offset,
                filter.PageSize));
        }
    }

    private static TelemetryStorePage<T> ToPage<T>(
        IEnumerable<T> source,
        int offset,
        int pageSize)
    {
        var items = source.Skip(offset).Take(pageSize + 1).ToList();
        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, hasMore);
    }

    private static bool Matches(string query, params string[] values) =>
        query.Length == 0
        || values.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase));
}
