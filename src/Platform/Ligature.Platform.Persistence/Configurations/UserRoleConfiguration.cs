using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class UserRoleConfiguration
    : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable(
            "user_role",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_user_role_actor_type",
                    "\"actor_type\" IN ('Human', 'Agent', 'System')");

                table.HasCheckConstraint(
                    "ck_user_role_scope",
                    "(\"scope_type\" = 'Global' AND \"scope_id\" IS NULL) OR " +
                    "(\"scope_type\" <> 'Global' AND \"scope_id\" IS NOT NULL)");

                table.HasCheckConstraint(
                    "ck_user_role_effective_period",
                    "\"effective_to\" IS NULL OR \"effective_to\" > \"effective_from\"");

                table.HasCheckConstraint(
                    "ck_user_role_revocation_pair",
                    "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");

                table.HasCheckConstraint(
                    "ck_user_role_revocation_reason",
                    "\"revoked_at\" IS NULL OR \"revocation_reason\" IS NOT NULL");

                table.HasCheckConstraint(
                    "ck_user_role_agent_finite",
                    "\"actor_type\" <> 'Agent' OR \"effective_to\" IS NOT NULL");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserRoleId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.RoleId)
            .HasConversion(
                new StronglyTypedIdValueConverter<RoleId>())
            .HasColumnName("role_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion(
                scopeType => scopeType.Value,
                value => ScopeType.Create(value))
            .IsRequired();

        builder.Property(x => x.ScopeId)
            .HasColumnName("scope_id")
            .HasColumnType("uuid");

        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.EffectiveTo)
            .HasColumnName("effective_to")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.AssignedBy)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("assigned_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.AssignmentReason)
            .HasColumnName("assignment_reason")
            .HasColumnType("text");

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.RevokedBy)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("revoked_by")
            .HasColumnType("uuid");

        builder.Property(x => x.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasColumnType("text");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.UserId,
                    x.ActorType
                })
            .HasPrincipalKey(
                x => new
                {
                    x.Id,
                    x.ActorType
                })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}