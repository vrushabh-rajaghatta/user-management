using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class UserIdentityConfiguration
    : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.ToTable("user_identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserIdentityId>())
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

        builder.Property(x => x.IdentityType)
            .HasColumnName("identity_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.IdentityProvider)
            .HasColumnName("identity_provider")
            .HasConversion(
                provider => provider.Value,
                value => IdentityProvider.Create(value))
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.SubjectId)
            .HasColumnName("subject_id")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.Username)
            .HasColumnName("username")
            .HasColumnType("varchar");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

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

        builder.ComplexProperty(
            x => x.Deactivation,
            deactivation =>
            {
                deactivation.Property(x => x.At)
                    .HasColumnName("deactivated_at")
                    .HasColumnType("timestamp with time zone");

                deactivation.Property(x => x.By)
                    .HasConversion(
                        new StronglyTypedIdValueConverter<UserId>())
                    .HasColumnName("deactivated_by")
                    .HasColumnType("uuid");
            });

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

        builder.HasIndex(x => new
        {
            x.IdentityProvider,
            x.SubjectId
        })
        .IsUnique();
    }
}