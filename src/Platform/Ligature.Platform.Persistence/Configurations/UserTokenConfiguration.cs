using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class UserTokenConfiguration
    : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable(
    "user_token",
    table =>
    {
        table.HasCheckConstraint(
            "ck_user_token_token_type",
            "\"token_type\" IN ('Activation', 'PasswordReset')");

        table.HasCheckConstraint(
            "ck_user_token_expires_after_created",
            "\"expires_at\" > \"created_at\"");

        table.HasCheckConstraint(
            "ck_user_token_not_used_and_invalidated",
            "NOT (\"used_at\" IS NOT NULL AND \"invalidated_at\" IS NOT NULL)");
    });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserTokenId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.UserIdentityId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserIdentityId>())
            .HasColumnName("user_identity_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.TokenType)
            .HasColumnName("token_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.InvalidatedAt)
            .HasColumnName("invalidated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
    .HasColumnName("created_at")
    .HasColumnType("timestamp with time zone")
    .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion<StronglyTypedIdValueConverter<UserId>>()
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserIdentity>()
            .WithMany()
            .HasForeignKey(x => x.UserIdentityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}