using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable(
            "app_user",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_app_user_actor_type",
                    "\"actor_type\" IN ('Human', 'Agent', 'System')");

                table.HasCheckConstraint(
                    "ck_app_user_status",
                    "\"status\" IN ('Active', 'Inactive')");

                table.HasCheckConstraint(
                    "ck_app_user_human_names",
                    "\"actor_type\" <> 'Human' OR (\"first_name\" IS NOT NULL AND \"last_name\" IS NOT NULL)");

                table.HasCheckConstraint(
                    "ck_app_user_deactivation_pair",
                    "(\"deactivated_at\" IS NULL) = (\"deactivated_by\" IS NULL)");

                table.HasCheckConstraint(
                    "ck_app_user_system_not_deactivated",
                    "\"actor_type\" <> 'System' OR \"deactivated_at\" IS NULL");

                // Mirrors char.IsControl in EmailAddress exactly, so the
                // pre-check and the constraint cannot disagree. Explicit
                // ranges rather than [[:cntrl:]], which is locale-dependent
                // and would make the invariant mean different things in
                // different environments.
                //
                // A raw string literal because the regex contains backslashes;
                // the other constraints here have none. The IS NULL disjunct is
                // spelled out rather than relying on NULL !~ ... yielding
                // unknown, since email is nullable.
                //
                // \x00 appears in the range for completeness. PostgreSQL cannot
                // store U+0000 in a varchar at all — it is refused at the text
                // encoding boundary with 22021 — so that character never
                // reaches this constraint.
                table.HasCheckConstraint(
                    "ck_app_user_email_no_control_characters",
                    """
                    "email" IS NULL OR "email" !~ '[\x00-\x1F\x7F-\x9F]'
                    """);
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.FirstName)
            .HasColumnName("first_name")
            .HasColumnType("varchar");

        builder.Property(x => x.LastName)
            .HasColumnName("last_name")
            .HasColumnType("varchar");

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasConversion(
                email => email == null ? null : email.Value,
                value => value == null ? null : EmailAddress.Create(value))
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

        builder.Property<DateTimeOffset>("UpdatedAt")
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property<UserId>("UpdatedBy")
            .HasColumnName("updated_by")
            .HasConversion(
                new StronglyTypedIdValueConverter<UserId>())
            .HasColumnType("uuid")
            .IsRequired();

        // AU8 — required for composite foreign-key support.
        builder.HasIndex(x => new
        {
            x.Id,
            x.ActorType
        })
        .HasDatabaseName("ux_app_user_id_actor_type")
        .IsUnique();
    }
}