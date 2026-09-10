using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

/// <summary>
/// Maps the notification table and the five declarative constraints that make
/// its six legal row shapes the only reachable ones (N3–N7).
///
/// N4–N6 are written in the NULL-safe `IS NOT DISTINCT FROM` form adopted after
/// the audit CHECK defects: a three-valued comparison that silently passes on
/// NULL is not a constraint. Status is NOT NULL, so the form is belt-and-braces
/// on N4 and N5 and load-bearing on N6, where not_sent_reason is nullable.
///
/// The partial indexes, the two triggers (N8/N9, N10) and the grants (N11) are
/// not expressible here and live in the migration alongside this mapping. There
/// is deliberately no DbSet: the context discovers configurations from the
/// assembly, and the notification table has exactly one writer.
/// </summary>
public sealed class NotificationConfiguration
    : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable(
            "notification",
            table =>
            {
                // N3 — release-controlled sets. Widened by migration, never
                // narrowed while rows reference a value.
                table.HasCheckConstraint(
                    "ck_notification_type",
                    "\"notification_type\" IN ('AccountActivation', 'PasswordReset', 'AdminPasswordReset')");

                table.HasCheckConstraint(
                    "ck_notification_status",
                    "\"status\" IN ('Pending', 'Sent', 'NotSent')");

                table.HasCheckConstraint(
                    "ck_notification_not_sent_reason",
                    "\"not_sent_reason\" IN ('TokenNotLive', 'SubjectInactive', 'TransportFailed', 'Abandoned')");

                // N4 — a reason is present if and only if the row is NotSent.
                table.HasCheckConstraint(
                    "ck_notification_reason_iff_not_sent",
                    "((\"status\" = 'NotSent') IS NOT DISTINCT FROM (\"not_sent_reason\" IS NOT NULL))");

                // N5 — a terminal row carries the instant it was closed, and a
                // Pending row cannot.
                table.HasCheckConstraint(
                    "ck_notification_closed_at_iff_terminal",
                    "((\"status\" <> 'Pending') IS NOT DISTINCT FROM (\"closed_at\" IS NOT NULL))");

                // N6 — every terminal row except Abandoned carries an attempt
                // time; Pending and Abandoned carry none. COALESCE is what
                // makes the right-hand side false rather than NULL for a
                // Pending row, whose reason is NULL.
                table.HasCheckConstraint(
                    "ck_notification_attempted_at_shape",
                    "((\"attempted_at\" IS NOT NULL) IS NOT DISTINCT FROM (\"status\" = 'Sent' OR COALESCE(\"not_sent_reason\" IN ('TokenNotLive', 'SubjectInactive', 'TransportFailed'), false)))");

                // N7 — an acceptance reference is meaningless unless the
                // transport accepted.
                table.HasCheckConstraint(
                    "ck_notification_message_id_only_when_sent",
                    "(\"status\" = 'Sent' OR \"transport_message_id\" IS NULL)");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<NotificationId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.NotificationType)
            .HasColumnName("notification_type")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.TokenId)
            .HasConversion(
                new StronglyTypedIdValueConverter<UserTokenId>())
            .HasColumnName("token_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Recipient)
            .HasColumnName("recipient")
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasColumnType("varchar")
            .IsRequired();

        builder.Property(x => x.NotSentReason)
            .HasColumnName("not_sent_reason")
            .HasConversion<string>()
            .HasColumnType("varchar");

        builder.Property(x => x.AttemptedAt)
            .HasColumnName("attempted_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.TransportMessageId)
            .HasColumnName("transport_message_id")
            .HasColumnType("varchar");

        // Defaulted by the database, not the application clock: the sweeper
        // compares this against the database's now(), and one clock on both
        // sides of that comparison is what keeps the grace window honest.
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd()
            .IsRequired();

        // N2 — at most one notification per issued token. Token ids are never
        // reused, so a purged row cannot be followed by a second insert for
        // the same token.
        builder.HasIndex(x => x.TokenId)
            .IsUnique()
            .HasDatabaseName("ux_notification_token_id");

        // Restrict rather than NoAction to match every other foreign key in
        // this model. N2 says "no referential action", meaning do not cascade
        // and do not set null; both satisfy that, and no role holds DELETE on
        // user_token, so neither can ever fire.
        builder.HasOne<UserToken>()
            .WithMany()
            .HasForeignKey(x => x.TokenId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
