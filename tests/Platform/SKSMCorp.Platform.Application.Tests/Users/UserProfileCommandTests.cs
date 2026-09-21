using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Commands.UpdateUserProfile;
using SKSMCorp.Platform.Application.Users.Queries.UserProfile;
using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Tests.Users;

/// <summary>
/// USR-C2 and USR-Q1 v1 declarations (docs/requirements.md, "USR-C2 — Update
/// User Profile, and USR-Q1 GetUser (narrow v1)"): what the pipeline enforces
/// before either handler runs.
/// </summary>
public sealed class UserProfileCommandTests
{
    [Fact]
    public void UpdateUserProfile_requires_user_update()
        => Assert.Equal("user.update", new UpdateUserProfileCommand(UserId.New(), "Ada", "Lovelace", "Ada").RequiredPermission);

    /// <summary>G1 — the catalogue does not mark user.update human-only, so neither is the command.</summary>
    [Fact]
    public void UpdateUserProfile_is_not_human_actor_only()
        => Assert.False(typeof(IHumanActorOnlyCommand<UpdateUserProfileResult>).IsAssignableFrom(typeof(UpdateUserProfileCommand)));

    [Fact]
    public void GetUser_requires_user_read()
        => Assert.Equal("user.read", UserProfileQuery.Authorization.PermissionCode);
}
