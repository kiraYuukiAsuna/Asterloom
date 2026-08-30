using Asterloom.Modules.Feature.Model;
using Asterloom.Modules.Outbox;

namespace Asterloom.Modules.Feature.Persistence;

public interface IFeatureStore
{
    Task<FeatureStorePage<FeatureFlag>> ListFlagsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        FeaturePageRequest page,
        CancellationToken cancellationToken);

    Task<FeatureFlag?> GetFlagAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid flagId,
        CancellationToken cancellationToken);

    Task<FeatureFlag?> GetFlagByKeyAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        string key,
        CancellationToken cancellationToken);

    Task<bool> TryCreateFlagAsync(
        FeatureFlag flag,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateFlagAsync(
        FeatureFlag flag,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task<FeatureStorePage<FeatureRevision>> ListRevisionsAsync(
        Guid flagId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);

    Task<FeatureRevision?> GetRevisionAsync(
        Guid flagId,
        long revision,
        CancellationToken cancellationToken);

    Task<bool> TryPublishAsync(
        FeatureFlag flag,
        long expectedVersion,
        FeatureRevision revision,
        OutboxMessageDraft integrationEvent,
        CancellationToken cancellationToken);
}
