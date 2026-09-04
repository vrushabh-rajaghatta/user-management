using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class RolePermissionConfiguration
    : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable(
            "role_permission",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_role_permission_revocation_pair",
                    "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<RolePermissionId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.RoleId)
            .HasConversion(
                new StronglyTypedIdValueConverter<RoleId>())
            .HasColumnName("role_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.PermissionId)
            .HasConversion(
                new StronglyTypedIdValueConverter<PermissionId>())
            .HasColumnName("permission_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.GrantedAt)
            .HasColumnName("granted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.GrantedBy)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("granted_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.RevokedBy)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("revoked_by")
            .HasColumnType("uuid");

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GrantedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}