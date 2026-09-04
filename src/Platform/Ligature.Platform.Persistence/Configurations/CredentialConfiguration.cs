using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class CredentialConfiguration
    : IEntityTypeConfiguration<Credential>
{
    public void Configure(EntityTypeBuilder<Credential> builder)
    {
        builder.ToTable("credential");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<CredentialId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.UserIdentityId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserIdentityId>())
            .HasColumnName("user_identity_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.IdentityType)
            .HasColumnName("identity_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.PasswordAlgorithm)
            .HasColumnName("password_algorithm")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.PasswordChangedAt)
            .HasColumnName("password_changed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.MustChangePassword)
            .HasColumnName("must_change_password")
            .HasColumnType("boolean")
            .IsRequired();

        builder.Property(x => x.FailedAttemptCount)
            .HasColumnName("failed_attempt_count")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(x => x.LockedUntil)
            .HasColumnName("locked_until")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("created_by")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => new
            {
                x.CreatedBy
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserIdentity>()
            .WithMany()
            .HasForeignKey(
                x => new
                {
                    x.UserIdentityId,
                    x.IdentityType
                })
            .HasPrincipalKey(
                x => new
                {
                    x.Id,
                    x.IdentityType
                })
            .OnDelete(DeleteBehavior.Restrict);
    }
}