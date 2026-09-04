using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class PasswordHistoryConfiguration
    : IEntityTypeConfiguration<PasswordHistory>
{
    public void Configure(EntityTypeBuilder<PasswordHistory> builder)
    {
        builder.ToTable("password_history");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<PasswordHistoryId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.UserIdentityId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserIdentityId>())
            .HasColumnName("user_identity_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.PasswordAlgorithm)
            .HasColumnName("password_algorithm")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<UserIdentity>()
            .WithMany()
            .HasForeignKey(x => x.UserIdentityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}