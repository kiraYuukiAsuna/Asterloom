using Asterloom.Modules.Identity.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Asterloom.Modules.Identity.Persistence;

public sealed class AsterloomIdentityDbContext(
    DbContextOptions<AsterloomIdentityDbContext> options)
    : IdentityDbContext<AsterloomUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.HasDefaultSchema(IdentityPersistence.Schema);

        builder.Entity<AsterloomUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.Status).HasConversion<short>();
            entity.Property(user => user.Version).IsConcurrencyToken();
            entity.Property(user => user.CreatedAt).IsRequired();
            entity.Property(user => user.UpdatedAt).IsRequired();
            entity.HasIndex(user => new { user.Status, user.DisplayName });
        });
    }
}
