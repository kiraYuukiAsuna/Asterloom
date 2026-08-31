using System.Globalization;
using System.Text;
using Asterloom.Modules.Errors;
using Asterloom.Modules.Identity.Model;
using Asterloom.Modules.Identity.Persistence;
using Asterloom.Modules.Platform.Model;
using Asterloom.Modules.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Asterloom.Modules.Identity.Management;

public sealed class IdentityMembershipService(
    AsterloomIdentityDbContext database,
    IPlatformResourceStore platformStore,
    TimeProvider timeProvider) : IApplicationMembershipValidator
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    public async Task<IdentityPage<ManagedApplicationMembership>> ListAsync(
        int pageSize,
        string? pageToken,
        Guid? userId,
        Guid? tenantId,
        Guid? applicationId,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var size = pageSize <= 0 ? DefaultPageSize : pageSize;
        if (size > MaximumPageSize)
        {
            throw Invalid("pageSize", $"Page size cannot exceed {MaximumPageSize}.");
        }

        var offset = DecodePageToken(pageToken);
        var query = database.ApplicationMemberships.AsNoTracking();
        if (userId is not null)
        {
            query = query.Where(item => item.UserId == userId.Value);
        }

        if (tenantId is not null)
        {
            query = query.Where(item => item.TenantId == tenantId.Value);
        }

        if (applicationId is not null)
        {
            query = query.Where(item => item.ApplicationId == applicationId.Value);
        }

        if (!includeRemoved)
        {
            query = query.Where(item =>
                item.Status == AsterloomApplicationMembershipStatus.Active);
        }

        var rows = await query
            .OrderBy(item => item.ApplicationId)
            .ThenBy(item => item.UserId)
            .Skip(offset)
            .Take(size + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > size;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new IdentityPage<ManagedApplicationMembership>(
            rows.Select(ToManaged).ToArray(),
            hasMore ? EncodePageToken(offset + rows.Count) : string.Empty);
    }

    public async Task<ManagedApplicationMembership?> FindAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var membership = await database.ApplicationMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.ApplicationId == applicationId,
                cancellationToken);
        return membership is null ? null : ToManaged(membership);
    }

    public async Task<bool> IsActiveMemberAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken) =>
        await database.ApplicationMemberships.AsNoTracking().AnyAsync(
            item => item.UserId == userId
                && item.ApplicationId == applicationId
                && item.Status == AsterloomApplicationMembershipStatus.Active,
            cancellationToken);

    public async Task<ManagedApplicationMembership> RequireActiveAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var membership = await FindAsync(userId, applicationId, cancellationToken);
        return membership is { Status: AsterloomApplicationMembershipStatus.Active }
            ? membership
            : throw new AsterloomException(
                AsterloomErrorKind.PermissionDenied,
                "identity_application_membership_required",
                "The account is not an active member of this application.");
    }

    public async Task<ManagedApplicationMembership> SetAsync(
        Guid userId,
        Guid tenantId,
        Guid applicationId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireId(userId, "userId");
        RequireId(tenantId, "tenantId");
        RequireId(applicationId, "applicationId");
        await ValidateApplicationAsync(tenantId, applicationId, cancellationToken);
        if (!await database.Users.AnyAsync(user => user.Id == userId, cancellationToken))
        {
            throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "identity_user_not_found",
                "The user was not found.");
        }

        var membership = await database.ApplicationMemberships.SingleOrDefaultAsync(
            item => item.ApplicationId == applicationId && item.UserId == userId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (membership is null)
        {
            if (expectedVersion != 0)
            {
                throw Conflict();
            }

            membership = new AsterloomApplicationMembership
            {
                UserId = userId,
                TenantId = tenantId,
                ApplicationId = applicationId,
                Status = AsterloomApplicationMembershipStatus.Active,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            database.ApplicationMemberships.Add(membership);
        }
        else
        {
            RequireVersion(membership.Version, expectedVersion);
            if (membership.Status == AsterloomApplicationMembershipStatus.Active
                && membership.TenantId == tenantId)
            {
                return ToManaged(membership);
            }

            membership.TenantId = tenantId;
            membership.Status = AsterloomApplicationMembershipStatus.Active;
            membership.Version++;
            membership.UpdatedAt = now;
        }

        await SaveAsync(cancellationToken);
        return ToManaged(membership);
    }

    internal async Task ValidateApplicationAsync(
        Guid tenantId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var tenant = await platformStore.GetTenantAsync(tenantId, cancellationToken);
        var application = await platformStore.GetApplicationAsync(
            tenantId,
            applicationId,
            cancellationToken);
        if (tenant is null || application is null)
        {
            throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "identity_platform_application_not_found",
                "The platform application binding was not found.");
        }

        if (tenant.Status != PlatformResourceStatus.Active
            || application.Status != PlatformResourceStatus.Active)
        {
            throw new AsterloomException(
                AsterloomErrorKind.FailedPrecondition,
                "identity_platform_application_inactive",
                "The platform application binding is not active.");
        }
    }

    public async Task<ManagedApplicationMembership> RemoveAsync(
        Guid userId,
        Guid applicationId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireId(userId, "userId");
        RequireId(applicationId, "applicationId");
        var membership = await database.ApplicationMemberships.SingleOrDefaultAsync(
                item => item.ApplicationId == applicationId && item.UserId == userId,
                cancellationToken)
            ?? throw new AsterloomException(
                AsterloomErrorKind.NotFound,
                "identity_application_membership_not_found",
                "The application membership was not found.");
        RequireVersion(membership.Version, expectedVersion);
        if (membership.Status == AsterloomApplicationMembershipStatus.Removed)
        {
            return ToManaged(membership);
        }

        membership.Status = AsterloomApplicationMembershipStatus.Removed;
        membership.Version++;
        membership.UpdatedAt = timeProvider.GetUtcNow();
        await SaveAsync(cancellationToken);
        return ToManaged(membership);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AsterloomException(
                AsterloomErrorKind.Conflict,
                "identity_application_membership_conflict",
                "The application membership was changed by another request.",
                innerException: exception);
        }
        catch (DbUpdateException exception)
        {
            throw new AsterloomException(
                AsterloomErrorKind.Conflict,
                "identity_application_membership_conflict",
                "The application membership could not be saved.",
                innerException: exception);
        }
    }

    private static ManagedApplicationMembership ToManaged(
        AsterloomApplicationMembership membership) =>
        new(
            membership.UserId,
            membership.TenantId,
            membership.ApplicationId,
            membership.Status,
            membership.Version,
            membership.CreatedAt,
            membership.UpdatedAt);

    private static void RequireId(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw Invalid(field, "A valid identifier is required.");
        }
    }

    private static void RequireVersion(long actual, long expected)
    {
        if (expected <= 0 || actual != expected)
        {
            throw Conflict();
        }
    }

    private static AsterloomException Conflict() => new(
        AsterloomErrorKind.Conflict,
        "identity_application_membership_conflict",
        "The application membership was changed by another request.");

    private static AsterloomException Invalid(string field, string message) => new(
        AsterloomErrorKind.InvalidArgument,
        "validation_failed",
        "One or more fields are invalid.",
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [field] = [message],
        });

    private static string EncodePageToken(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            offset.ToString(CultureInfo.InvariantCulture)));

    private static int DecodePageToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var offset)
                && offset >= 0
                ? offset
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw Invalid("pageToken", "The page token is invalid.");
        }
    }
}
