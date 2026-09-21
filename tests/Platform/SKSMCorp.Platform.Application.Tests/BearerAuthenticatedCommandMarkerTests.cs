using System.Reflection;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Commands.ActivateAccount;
using SKSMCorp.Platform.Application.Users.Commands.ResetPassword;
using SKSMCorp.Platform.Application.Users.Commands.SignIn;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Tests;

/// <summary>
/// The marker is only as good as its coverage. AuthenticationBehavior refuses a
/// bearer-authenticated identity-establishing command under an established
/// caller — but only if the command says it is one. A new command whose handler
/// establishes its caller from a credential, and which forgot the marker, would
/// silently reopen the defect this exists to close.
///
/// So the marker is tied to what actually establishes a caller: a handler that
/// takes IBearerActorEstablisher. Checked in both directions, and against the
/// exact set decided (S1), so the marker can neither be forgotten nor spread to
/// a command that establishes nobody.
/// </summary>
public sealed class BearerAuthenticatedCommandMarkerTests
{
    private static readonly Assembly Application = typeof(SignInCommandHandler).Assembly;

    [Fact]
    public void Every_handler_that_establishes_a_bearer_handles_a_marked_command()
    {
        var establishing = HandlersTaking<IBearerActorEstablisher>();

        Assert.NotEmpty(establishing);

        foreach (var (handler, command) in establishing)
        {
            Assert.True(
                IsMarked(command),
                $"{handler.Name} establishes its caller through IBearerActorEstablisher, "
                + $"but {command.Name} does not declare IBearerAuthenticatedCommand. "
                + "Without it AuthenticationBehavior lets the command start under an "
                + "established caller.");
        }
    }

    [Fact]
    public void Every_marked_command_is_handled_by_a_handler_that_establishes_a_bearer()
    {
        var establishingCommands = HandlersTaking<IBearerActorEstablisher>()
            .Select(x => x.Command)
            .ToHashSet();

        foreach (var command in Application.GetTypes().Where(IsMarked))
        {
            Assert.True(
                establishingCommands.Contains(command),
                $"{command.Name} declares IBearerAuthenticatedCommand, but no handler for it "
                + "establishes a caller. The marker refuses signed-in callers; it belongs "
                + "only on commands that authenticate their own bearer.");
        }
    }

    /// <summary>
    /// The decided set, named. CRD-C2 RequestPasswordReset is anonymous and
    /// deliberately not in it: it establishes nobody.
    /// </summary>
    [Fact]
    public void The_marked_commands_are_exactly_sign_in_activation_and_password_reset()
    {
        var marked = Application.GetTypes().Where(IsMarked).ToHashSet();

        Assert.Equal(
            new HashSet<Type>
            {
                typeof(SignInCommand),
                typeof(ActivateAccountCommand),
                typeof(ResetPasswordCommand),
            },
            marked);
    }

    private static bool IsMarked(Type type)
        => !type.IsInterface
           && type.GetInterfaces().Any(x =>
               x.IsGenericType
               && x.GetGenericTypeDefinition() == typeof(IBearerAuthenticatedCommand<>));

    private static List<(Type Handler, Type Command)> HandlersTaking<TDependency>()
        => Application.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(TDependency))))
            .SelectMany(type => type.GetInterfaces()
                .Where(x =>
                    x.IsGenericType
                    && x.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                .Select(x => (Handler: type, Command: x.GetGenericArguments()[0])))
            .ToList();
}
