using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SKSMCorp.Platform.Persistence.Configurations;

public sealed class UserIdentityConfiguration
    : IEntityTypeConfiguration<UserIdentity>
{
    /// <summary>
    /// char.IsWhiteSpace, as a PostgreSQL bracket expression: U+0009-U+000D,
    /// U+0020, U+0085, U+00A0, U+1680, U+2000-U+200A, U+2028, U+2029, U+202F,
    /// U+205F and U+3000. The escapes are the regex engine's, not C#'s.
    /// </summary>
    private const string Whitespace =
        @"[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]";

    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.ToTable(
            "user_identity",
            table =>
            {
                // USR-C4/C5 D12, as on app_user. UI9's pair check lives in a
                // raw-SQL migration; this closes the triangle beside it.
                table.HasCheckConstraint(
                    "ck_user_identity_status_deactivation",
                    "(\"status\" = 'Inactive') = (\"deactivated_at\" IS NOT NULL)");

                // UI6 (entities workbook): a local identity has a username,
                // and it is not empty. External identities carry whatever
                // their provider asserts, or nothing.
                table.HasCheckConstraint(
                    "ck_user_identity_local_username_required",
                    "\"identity_type\" <> 'Local' OR (\"username\" IS NOT NULL AND \"username\" <> '')");

                // "Local usernames refuse surrounding whitespace" (WS3): the
                // BACKSTOP for UserIdentity.ValidateUsernameBoundary, which is
                // the rule. The class names char.IsWhiteSpace's 25 characters
                // explicitly rather than a locale-dependent [[:space:]], so the
                // constraint means the same thing on every server; a test
                // evaluates it for every UTF-16 code unit against the domain.
                table.HasCheckConstraint(
                    "ck_user_identity_local_username_no_surrounding_whitespace",
                    $"\"identity_type\" <> 'Local' OR \"username\" IS NULL "
                    + $"OR \"username\" !~ '^{Whitespace}|{Whitespace}$'");
            });

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