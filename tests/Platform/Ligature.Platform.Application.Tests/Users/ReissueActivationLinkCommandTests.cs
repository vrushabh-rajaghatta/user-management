using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Commands.ReissueActivationLink;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// CRD-C7's declarations: what the pipeline enforces before the handler runs.
/// The behaviour they cause is proven against PostgreSQL; these pin the
/// declarations themselves, so a changed permission is a failing test here and
/// not only a differently-refused caller there.
/// </summary>
public sealed class ReissueActivationLinkCommandTests
{
    /// <summary>
    /// user.create, by owner decision (docs/requirements.md, CRD-C7): reissuing
    /// the activation token finishes the work creation started and grants
    /// nothing, so it is not a separate permission.
    /// </summary>
    [Fact]
    public void It_requires_user_create()
    {
        var command = new ReissueActivationLinkCommand(UserId.New(), "Mail never arrived.");

        Assert.Equal("user.create", command.RequiredPermission);
    }

    [Fact]
    public void It_is_a_human_actor_only_command()
    {
        Assert.True(
            typeof(IHumanActorOnlyCommand<ReissueActivationLinkResult>)
                .IsAssignableFrom(typeof(ReissueActivationLinkCommand)));
    }
}
