using System.Linq.Expressions;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.Platform.Persistence.Database;

namespace SKSMCorp.Platform.Persistence.Services;

/// <summary>
/// What a user IS, as both the list (USR-Q2) and the detail (USR-Q1 GetUser
/// v2) read it: ONE projection, so the two views cannot disagree about a user's
/// email, status or activationPending (docs/requirements.md, "USR-Q1 GetUser v2
/// and the User detail page", DV-1). Each reader chooses which users and in
/// what order; neither restates what these fields mean.
/// </summary>
internal sealed record UserLifecycleFacts(
    UserId UserId,
    string? FirstName,
    string? LastName,
    string DisplayName,
    EmailAddress? Email,
    UserStatus Status,
    bool ActivationPending);

internal static class UserLifecycleProjection
{
    /// <summary>
    /// The projection, for a Select over users already chosen and ordered by
    /// the caller. It adds no rows and changes no order.
    ///
    /// Status is the stored lifecycle status, exactly (USR-Q2 amendment 2).
    /// ActivationPending is USR-Q2 amendment 1 (D1), exactly and nothing
    /// broader: at least one local identity, and no credential on any identity
    /// of the user. False means only "not pending".
    /// </summary>
    internal static Expression<Func<User, UserLifecycleFacts>> Of(SKSMCorpDbContext dbContext)
        => user => new UserLifecycleFacts(
            user.Id,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.Email,
            user.Status,
            dbContext.Set<UserIdentity>().Any(i =>
                i.UserId == user.Id && i.IdentityType == IdentityType.Local)
            && !dbContext.Set<UserIdentity>()
                .Where(i => i.UserId == user.Id)
                .Any(i => dbContext.Set<Credential>().Any(c => c.UserIdentityId == i.Id)));
}
