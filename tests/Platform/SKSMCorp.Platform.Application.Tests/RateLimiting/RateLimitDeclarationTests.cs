using System.Reflection;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.RateLimiting;
using SKSMCorp.Platform.Application.Users.Commands.ActivateAccount;
using SKSMCorp.Platform.Application.Users.Commands.RequestPasswordReset;
using SKSMCorp.Platform.Application.Users.Commands.ResetPassword;
using SKSMCorp.Platform.Application.Users.Commands.SignIn;

namespace SKSMCorp.Platform.Application.Tests.RateLimiting;

/// <summary>
/// What each command declares (B2), the frozen numbers (B4), and RL-13: every
/// anonymous command is limited, and nothing else is.
/// </summary>
public sealed class RateLimitDeclarationTests
{
    private static readonly Assembly Application = typeof(SignInCommand).Assembly;

    /// <summary>RL-13: a future anonymous command cannot be added unlimited.</summary>
    [Fact]
    public void Every_anonymous_command_declares_its_rate_limits()
    {
        var anonymous = Application.GetTypes().Where(IsAnonymousCommand).ToList();

        Assert.NotEmpty(anonymous);

        foreach (var command in anonymous)
        {
            Assert.True(
                typeof(IRateLimitedCommand).IsAssignableFrom(command),
                $"{command.Name} is an IAnonymousCommand but does not declare "
                + "IRateLimitedCommand. Behaviour 11 applies to every anonymous "
                + "command (docs/requirements.md, \"Behaviour 11\").");
        }
    }

    /// <summary>RL-13: authenticated commands are never limited by this behaviour.</summary>
    [Fact]
    public void Only_anonymous_commands_declare_rate_limits()
    {
        foreach (var command in Application.GetTypes()
                     .Where(x => !x.IsInterface && typeof(IRateLimitedCommand).IsAssignableFrom(x)))
        {
            Assert.True(
                IsAnonymousCommand(command),
                $"{command.Name} declares rate limits but is not anonymous.");
        }
    }

    [Fact]
    public void The_limited_commands_are_exactly_the_four_anonymous_ones()
        => Assert.Equal(
            new HashSet<Type>
            {
                typeof(SignInCommand),
                typeof(ActivateAccountCommand),
                typeof(RequestPasswordResetCommand),
                typeof(ResetPasswordCommand),
            },
            Application.GetTypes()
                .Where(x => !x.IsInterface && typeof(IRateLimitedCommand).IsAssignableFrom(x))
                .ToHashSet());

    // ------------------------------------------------ what each declares (B2)

    [Fact]
    public void Sign_in_is_limited_by_username_and_by_client_address()
        => AssertDeclares(
            new SignInCommand("Ada ", "a-password", "203.0.113.7", "agent"),
            (RateLimitRules.SignInByUsername, "Ada "),
            (RateLimitRules.SignInByClientAddress, "203.0.113.7"));

    [Fact]
    public void Forgot_password_is_limited_by_the_typed_address_and_by_client_address()
        => AssertDeclares(
            new RequestPasswordResetCommand("Ada@Example.test", "203.0.113.7"),
            (RateLimitRules.PasswordResetRequestByAddress, "Ada@Example.test"),
            (RateLimitRules.PasswordResetRequestByClientAddress, "203.0.113.7"));

    [Fact]
    public void Reset_password_is_limited_by_client_address_alone()
        => AssertDeclares(
            new ResetPasswordCommand("token", "a-password", "203.0.113.7"),
            (RateLimitRules.ResetPasswordByClientAddress, "203.0.113.7"));

    [Fact]
    public void Activate_is_limited_by_client_address_alone()
        => AssertDeclares(
            new ActivateAccountCommand("token", "a-password", "203.0.113.7"),
            (RateLimitRules.ActivateAccountByClientAddress, "203.0.113.7"));

    /// <summary>The host passes "unknown" through as null; the command never invents one.</summary>
    [Fact]
    public void An_unknown_client_address_is_declared_as_null()
        => AssertDeclares(
            new ActivateAccountCommand("token", "a-password"),
            (RateLimitRules.ActivateAccountByClientAddress, null));

    // ------------------------------------------------ the frozen numbers (B4)

    public static TheoryData<RateLimitRule, RateLimitKeyKind, int, TimeSpan> FrozenLimits => new()
    {
        { RateLimitRules.SignInByUsername, RateLimitKeyKind.Address, 10, TimeSpan.FromMinutes(15) },
        { RateLimitRules.SignInByClientAddress, RateLimitKeyKind.ClientAddress, 30, TimeSpan.FromMinutes(1) },
        { RateLimitRules.PasswordResetRequestByAddress, RateLimitKeyKind.Address, 3, TimeSpan.FromHours(1) },
        { RateLimitRules.PasswordResetRequestByClientAddress, RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromHours(1) },
        { RateLimitRules.ResetPasswordByClientAddress, RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromMinutes(15) },
        { RateLimitRules.ActivateAccountByClientAddress, RateLimitKeyKind.ClientAddress, 10, TimeSpan.FromMinutes(15) },
    };

    [Theory]
    [MemberData(nameof(FrozenLimits))]
    public void The_limits_are_the_frozen_ones(
        RateLimitRule rule, RateLimitKeyKind kind, int limit, TimeSpan window)
    {
        Assert.Equal(kind, rule.Kind);
        Assert.Equal(limit, rule.Limit);
        Assert.Equal(window, rule.Window);
    }

    /// <summary>Each (command, key kind) pair is its own bucket.</summary>
    [Fact]
    public void Every_rule_is_a_distinct_bucket()
    {
        var rules = new[]
        {
            RateLimitRules.SignInByUsername,
            RateLimitRules.SignInByClientAddress,
            RateLimitRules.PasswordResetRequestByAddress,
            RateLimitRules.PasswordResetRequestByClientAddress,
            RateLimitRules.ResetPasswordByClientAddress,
            RateLimitRules.ActivateAccountByClientAddress,
        };

        Assert.Equal(rules.Length, rules.Select(x => (x.Name, x.Kind)).Distinct().Count());
    }

    // ------------------------------------------------------------------

    private static void AssertDeclares(object command, params (RateLimitRule Rule, string? Value)[] expected)
    {
        var limited = Assert.IsAssignableFrom<IRateLimitedCommand>(command);

        Assert.Equal(
            expected.Select(x => new RateLimitSubject(x.Rule, x.Value)).ToHashSet(),
            limited.RateLimitSubjects.ToHashSet());

        Assert.Equal(expected.Length, limited.RateLimitSubjects.Count);
    }

    private static bool IsAnonymousCommand(Type type)
        => !type.IsInterface
           && !type.IsAbstract
           && type.GetInterfaces().Any(x =>
               x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IAnonymousCommand<>));
}
