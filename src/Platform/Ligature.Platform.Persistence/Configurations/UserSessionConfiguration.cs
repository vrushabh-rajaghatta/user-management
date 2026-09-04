using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class UserSessionConfiguration
    : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable(
            "user_session",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_user_session_last_activity_at",
                    "\"last_activity_at\" >= \"created_at\"");

                table.HasCheckConstraint(
                    "ck_user_session_expires_at",
                    "\"expires_at\" > \"created_at\"");

                table.HasCheckConstraint(
                    "ck_user_session_revocation_pair",
                    "(\"revoked_at\" IS NULL) = (\"revoked_by\" IS NULL)");

                table.HasCheckConstraint(
                    "ck_user_session_revocation_reason",
                    "\"revoked_at\" IS NULL OR \"revocation_reason\" IS NOT NULL");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserSessionId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.UserIdentityId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserIdentityId>())
            .HasColumnName("user_identity_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.LastActivityAt)
            .HasColumnName("last_activity_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

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

        builder.Property(x => x.IpAddress)
            .HasColumnName("ip_address")
            .HasColumnType("inet");

        builder.Property(x => x.UserAgent)
            .HasColumnName("user_agent")
            .HasColumnType("text");

        builder.HasOne<UserIdentity>()
            .WithMany()
            .HasForeignKey(x => x.UserIdentityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}