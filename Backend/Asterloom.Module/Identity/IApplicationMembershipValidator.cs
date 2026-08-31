namespace Asterloom.Modules.Identity;

public interface IApplicationMembershipValidator
{
    Task<bool> IsActiveMemberAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken);
}
