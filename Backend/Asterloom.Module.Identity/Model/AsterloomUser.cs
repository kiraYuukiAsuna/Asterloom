using Microsoft.AspNetCore.Identity;

namespace Asterloom.Modules.Identity.Model;

public enum AsterloomUserStatus : short
{
    Pending = 0,
    Active = 1,
    Suspended = 2,
    Archived = 3,
}

public sealed class AsterloomUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public AsterloomUserStatus Status { get; set; } = AsterloomUserStatus.Pending;

    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }
}

public enum AsterloomApplicationMembershipStatus : short
{
    Active = 1,
    Removed = 2,
}

public sealed class AsterloomApplicationMembership
{
    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public Guid ApplicationId { get; set; }

    public AsterloomApplicationMembershipStatus Status { get; set; } =
        AsterloomApplicationMembershipStatus.Active;

    public long Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public AsterloomUser User { get; set; } = null!;
}
