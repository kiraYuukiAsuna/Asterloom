using Asterloom.Modules.Targeting.Model;

namespace Asterloom.Modules.Targeting.Persistence;

public interface ITargetingStore
{
    Task<TargetingStorePage<TargetingSegment>> ListSegmentsAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        TargetingPageRequest page,
        CancellationToken cancellationToken);

    Task<TargetingSegment?> GetSegmentAsync(
        Guid tenantId,
        Guid applicationId,
        Guid environmentId,
        Guid segmentId,
        CancellationToken cancellationToken);

    Task<bool> TryCreateSegmentAsync(
        TargetingSegment segment,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateSegmentAsync(
        TargetingSegment segment,
        long expectedVersion,
        CancellationToken cancellationToken);
}
