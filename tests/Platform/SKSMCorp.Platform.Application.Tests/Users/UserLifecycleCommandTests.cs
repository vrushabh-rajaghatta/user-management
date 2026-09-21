using System.Reflection;
using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Audit;
using SKSMCorp.Platform.Application.Users.Commands.DeactivateUser;
using SKSMCorp.Platform.Application.Users.Commands.ReactivateUser;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Tests.Users;

/// <summary>
/// USR-C4 / USR-C5 declarations, and the reason rule (docs/requirements.md,
/// "USR-C4 / USR-C5"): what the pipeline enforces before either handler runs,
/// and that a blank reason is refused BEFORE ANY DATABASE WORK.
///
/// Every database interaction goes through the unit of work, so no
/// transaction opened means no database work. Every other collaborator is a
/// proxy that throws if touched at all.
/// </summary>
public sealed class UserLifecycleCommandTests
{
    [Fact]
    public void DeactivateUser_requires_user_deactivate()
        => Assert.Equal("user.deactivate", new DeactivateUserCommand(UserId.New(), "Left the company.").RequiredPermission);

    [Fact]
    public void ReactivateUser_requires_user_reactivate()
        => Assert.Equal("user.reactivate", new ReactivateUserCommand(UserId.New(), "Rehired.").RequiredPermission);

    [Fact]
    public void DeactivateUser_is_human_actor_only()
        => Assert.True(typeof(IHumanActorOnlyCommand<DeactivateUserResult>).IsAssignableFrom(typeof(DeactivateUserCommand)));

    [Fact]
    public void ReactivateUser_is_human_actor_only()
        => Assert.True(typeof(IHumanActorOnlyCommand<ReactivateUserResult>).IsAssignableFrom(typeof(ReactivateUserCommand)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_deactivation_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new DeactivateUserCommandHandler(
            Untouched<IExecutionContext>(), Untouched<IClock>(), unitOfWork,
            Untouched<IUserRepository>(), Untouched<IUserIdentityRepository>(), Untouched<IUserRoleRepository>(),
            Untouched<IUserSessionRepository>(), Untouched<ISecurityPolicyResolver>(), Untouched<IUserTokenRepository>(),
            Untouched<IAuditEvents>());

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new DeactivateUserCommand(UserId.New(), reason), CancellationToken.None));

        Assert.Equal("A reason is required to deactivate a user.", refusal.Message);
        Assert.Equal(0, unitOfWork.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reactivation_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new ReactivateUserCommandHandler(
            unitOfWork,
            Untouched<IUserRepository>(), Untouched<IUserIdentityRepository>(), Untouched<IAuditEvents>());

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new ReactivateUserCommand(UserId.New(), reason), CancellationToken.None));

        Assert.Equal("A reason is required to reactivate a user.", refusal.Message);
        Assert.Equal(0, unitOfWork.Calls);
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Calls { get; private set; }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("A transaction was opened for a blank reason.");
        }
    }

    /// <summary>A collaborator the refusal must not reach: any member access throws.</summary>
    private static T Untouched<T>() where T : class
        => DispatchProxy.Create<T, TouchedProxy>();

    public class TouchedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException($"{targetMethod?.Name} was touched for a blank reason.");
    }
}
