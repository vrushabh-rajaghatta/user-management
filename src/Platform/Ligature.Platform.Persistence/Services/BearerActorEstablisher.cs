using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Ligature.Platform.Persistence.Services;

/// <summary>
/// AUD-D28's User Management half: the actor snapshot for a bearer whose
/// identity has just been established by consuming their token.
///
/// The projection is the one CallerEstablisher already builds, minus the
/// session and its two liveness checks, and keyed by identity instead. The
/// snapshot fields come from the rows this query joins, so the values
/// recorded are the ones that were true at the moment of establishment
/// (AUD-1), and nothing re-reads them later.
///
/// No authority is established. Nobody granted this caller anything: they
/// proved possession of a token, and the records they cause carry no
/// authorising role or assignment (AR12 columns stay null). Activation is an
/// anonymous command, so the authorisation behaviour never runs and there is
/// no authority to carry even if one existed.
/// </summary>
public sealed class BearerActorEstablisher : IBearerActorEstablisher
{
    private readonly LigatureDbContext _dbContext;
    private readonly IExecutionContext _executionContext;
    private readonly IExecutionContextInitializer _executionContextInitializer;
    private readonly IClock _clock;

    public BearerActorEstablisher(
        LigatureDbContext dbContext,
        IExecutionContext executionContext,
        IExecutionContextInitializer executionContextInitializer,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(executionContextInitializer);
        ArgumentNullException.ThrowIfNull(clock);

        _dbContext = dbContext;
        _executionContext = executionContext;
        _executionContextInitializer = executionContextInitializer;
        _clock = clock;
    }

    public async Task<bool> EstablishAsync(
        UserIdentityId identityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identityId);

        var snapshot = await LoadAsync(identityId, cancellationToken);

        if (snapshot is null)
            return false;

        // Status is deliberately NOT examined. The consumption query decides
        // whether the token was usable, and adding a second eligibility rule
        // here would change what activation accepts under cover of an audit
        // story (recorded as a User Management gap in docs/requirements.md).

        // A caller may already be established: a retried unit of work replays
        // the handler in the same scope, a scope may dispatch the command
        // twice, and a request carrying a live session establishes one before
        // the pipeline runs. The context refuses to be rebound — deliberately,
        // it is what stops an identity changing midway — so the question here
        // is whether the caller already established IS this bearer.
        //
        // Same user and same subject: nothing to do, and the records that
        // follow carry the actor this bearer authenticated as. Anyone else:
        // refuse, because the alternative is a record attributing the
        // activation to whoever happened to hold the scope.
        if (_executionContext.IsAuthenticated)
        {
            return _executionContext.UserId == snapshot.UserId
                && _executionContext.Identity.SubjectId == snapshot.SubjectId;
        }

        _executionContextInitializer.Establish(
            snapshot.UserId,
            snapshot.ActorType,
            new ActorIdentity(
                snapshot.DisplayName,
                snapshot.Username,
                snapshot.Email,
                snapshot.IdentityProvider,
                snapshot.SubjectId,
                CapturedAt: _clock.UtcNow));

        return true;
    }

    /// <summary>
    /// No tracking, for the reason CallerEstablisher documents at length: a
    /// tracked read here would hand the handler this instance instead of a
    /// fresh one, and a retried unit of work would then see the previous
    /// attempt's mutations.
    /// </summary>
    private async Task<Snapshot?> LoadAsync(
        UserIdentityId identityId,
        CancellationToken cancellationToken)
    {
        return await (
            from identity in _dbContext.Set<UserIdentity>().AsNoTracking()
            join user in _dbContext.Set<User>().AsNoTracking()
                on identity.UserId equals user.Id
            where identity.Id == identityId
            select new Snapshot(
                user.Id,
                user.ActorType,
                user.DisplayName,
                identity.Username,
                user.Email,
                identity.IdentityProvider,
                identity.SubjectId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record Snapshot(
        UserId UserId,
        ActorType ActorType,
        string DisplayName,
        string? Username,
        EmailAddress? Email,
        IdentityProvider IdentityProvider,
        string SubjectId);
}
