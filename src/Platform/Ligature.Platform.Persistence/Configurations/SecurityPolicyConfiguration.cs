using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ligature.Platform.Persistence.Configurations;

public sealed class SecurityPolicyConfiguration
    : IEntityTypeConfiguration<SecurityPolicy>
{
    public void Configure(EntityTypeBuilder<SecurityPolicy> builder)
    {
        builder.ToTable("security_policy");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new StronglyTypedIdValueConverter<SecurityPolicyId>())
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(x => x.PolicyVersion)
            .HasColumnName("policy_version")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.ComplexProperty(
            x => x.Settings,
            settings =>
            {
                settings.Property(x => x.PasswordMinLength)
                    .HasColumnName("password_min_length")
                    .HasColumnType("integer")
                    .IsRequired();

                settings.Property(x => x.PasswordHistoryDepth)
                    .HasColumnName("password_history_depth")
                    .HasColumnType("integer")
                    .IsRequired();

                settings.Property(x => x.MaxFailedLoginAttempts)
                    .HasColumnName("max_failed_login_attempts")
                    .HasColumnType("integer")
                    .IsRequired();

                settings.Property(x => x.LockoutDuration)
                    .HasColumnName("lockout_duration")
                    .HasColumnType("interval")
                    .IsRequired();

                settings.Property(x => x.ActivationTokenLifetime)
                    .HasColumnName("activation_token_lifetime")
                    .HasColumnType("interval")
                    .IsRequired();

                settings.Property(x => x.PasswordResetTokenLifetime)
                    .HasColumnName("password_reset_token_lifetime")
                    .HasColumnType("interval")
                    .IsRequired();

                settings.Property(x => x.SessionIdleTimeout)
                    .HasColumnName("session_idle_timeout")
                    .HasColumnType("interval")
                    .IsRequired();

                settings.Property(x => x.SessionAbsoluteTimeout)
                    .HasColumnName("session_absolute_timeout")
                    .HasColumnType("interval")
                    .IsRequired();
            });

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion<StronglyTypedIdValueConverter<UserId>>()
            .IsRequired();
        builder.HasIndex(x => x.PolicyVersion)
            .IsUnique();

        builder.HasIndex(x => x.EffectiveFrom)
            .IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}