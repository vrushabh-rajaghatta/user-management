using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ligature.Platform.Persistence.Database;

/// <summary>
/// Stamps the System-managed provenance columns (G4) on every write.
///
/// UpdatedAt and UpdatedBy are shadow properties: they are part of the mapping
/// rather than the domain model, because "when was this row last touched" is a
/// persistence fact, not a business rule the aggregate should be able to set.
/// Both are NOT NULL, so before this interceptor existed every writer had to
/// remember to stamp them by hand — which the provisioners did and the command
/// handlers did not.
///
/// Only entities that declare the shadow properties are stamped, so append-only
/// tables (security_policy, password_history, role_permission) are left alone
/// by construction rather than by an exclusion list that could drift.
/// </summary>
public sealed class ProvenanceStampingInterceptor : SaveChangesInterceptor
{
    private const string UpdatedAtProperty = "UpdatedAt";
    private const string UpdatedByProperty = "UpdatedBy";

    private readonly IClock _clock;
    private readonly IExecutionContext? _executionContext;

    /// <param name="executionContext">
    /// Null when the write originates outside an authenticated request —
    /// provisioning, migrations, maintenance. The interceptor deliberately does
    /// not know why it is absent; it only knows that the System actor owns any
    /// write no human authorised.
    /// </param>
    public ProvenanceStampingInterceptor(
        IClock clock,
        IExecutionContext? executionContext)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        _executionContext = executionContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var at = _clock.UtcNow;
        var by = ResolveActor();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var updatedAt = entry.Metadata.FindProperty(UpdatedAtProperty);

            if (updatedAt is null || !updatedAt.IsShadowProperty())
                continue;

            entry.Property(UpdatedAtProperty).CurrentValue = at;

            var updatedBy = entry.Metadata.FindProperty(UpdatedByProperty);

            if (updatedBy is not null && updatedBy.IsShadowProperty())
                entry.Property(UpdatedByProperty).CurrentValue = by;
        }
    }

    private UserId ResolveActor()
    {
        // An unauthenticated context is treated as no context at all: its UserId
        // carries no actor, and attributing a write to it would be a fabrication.
        if (_executionContext is null || !_executionContext.IsAuthenticated)
            return User.SystemUserId;

        return _executionContext.UserId;
    }
}
