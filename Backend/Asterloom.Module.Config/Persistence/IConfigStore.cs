using Asterloom.Modules.Config.Model;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Config.Persistence;

public interface IConfigStore
{
    Task<ConfigStorePage<ConfigEntry>> ListEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        ConfigPageRequest page,
        CancellationToken cancellationToken);

    Task<ConfigEntry?> GetEntryAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid entryId,
        CancellationToken cancellationToken);

    Task<ConfigEntry?> GetEntryByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConfigEntry>> ListPublishedEntriesAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateEntryAsync(
        ConfigEntry entry,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateEntryAsync(
        ConfigEntry entry,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<ConfigStorePage<ConfigRevision>> ListRevisionsAsync(
        Guid entryId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ConfigRevision?> GetRevisionAsync(
        Guid entryId,
        long revision,
        CancellationToken cancellationToken);

    Task<ConfigSnapshot?> GetLatestSnapshotAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken);

    Task<ConfigStorePage<ConfigSnapshot>> ListSnapshotsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);

    Task<bool> TryCommitSnapshotAsync(
        ConfigEntry entry,
        long expectedVersion,
        ConfigRevision? revision,
        ConfigSnapshot snapshot,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken);
}
