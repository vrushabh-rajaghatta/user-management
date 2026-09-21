using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Roles.Commands.DeactivateRole;
using Ligature.Platform.Application.Roles.Commands.RemovePermissionFromRole;
using Ligature.Platform.Application.Users.Commands.GrantRole;
using Ligature.Platform.Application.Users.Commands.RevokeRole;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Tests.Users;

/// <summary>
/// "A blank reason is refused BEFORE ANY DATABASE WORK" (docs/requirements.md,
/// "Role Assignment", G6), for every command that requires one: AUT-C1,
/// AUT-C2 and now AUT-C5. The domain would refuse a blank reason too, so the
/// integration suite cannot tell whether the database was touched first; this
/// can. Every database interaction goes through the unit of work, so no
/// transaction opened means no database work. Every other collaborator throws
/// if touched.
/// </summary>
public sealed class RoleAssignmentReasonTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_grant_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new GrantRoleCommandHandler(
            new Untouched(), new Untouched(), unitOfWork,
            new Untouched(), new Untouched(), new Untouched(), new Untouched());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new GrantRoleCommand(UserId.New(), RoleId.New(), null, null, reason),
            CancellationToken.None));

        Assert.Equal(0, unitOfWork.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_revocation_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new RevokeRoleCommandHandler(
            new Untouched(), new Untouched(), unitOfWork, new Untouched(), new Untouched());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new RevokeRoleCommand(UserRoleId.New(), reason),
            CancellationToken.None));

        Assert.Equal(0, unitOfWork.Calls);
    }

    /// <summary>
    /// AUT-C5 (RD5, RD-A4). The same proof for role deactivation, and the
    /// reason it matters concretely: RoleDeactivated is seeded
    /// ReasonRequired, so a blank reason that reached the audit assembler
    /// would fail AR9 as an emission defect — a 500, not a refusal.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_deactivation_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new DeactivateRoleCommandHandler(unitOfWork, new Untouched(), new Untouched());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new DeactivateRoleCommand(RoleId.New(), reason),
            CancellationToken.None));

        Assert.Equal(0, unitOfWork.Calls);
    }

    /// <summary>
    /// AUT-C8 (RG5, RG-A11). PermissionRevokedFromRole is seeded
    /// ReasonRequired, so a blank reason reaching the audit assembler would
    /// fail AR9 as an emission defect — a 500 rather than a refusal.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_permission_revocation_reason_is_refused_before_a_transaction_opens(string reason)
    {
        var unitOfWork = new CountingUnitOfWork();

        var handler = new RemovePermissionFromRoleCommandHandler(
            unitOfWork, new Untouched(), new Untouched(), new Untouched(),
            new Untouched(), new Untouched(), new Untouched(), new Untouched());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => handler.Handle(
            new RemovePermissionFromRoleCommand(RolePermissionId.New(), reason),
            CancellationToken.None));

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

    /// <summary>Every collaborator the refusal must not reach.</summary>
    private sealed class Untouched
        : IExecutionContext, IClock, IUserRepository, IRoleRepository, IUserRoleRepository,
          IRolePermissionRepository, IPermissionRepository, IAuditEvents
    {
        public Task<User?> FindForUpdateAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserRole>> FindForUserAsync(UserId userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public UserId UserId => throw Touched();
        public ActorType ActorType => throw Touched();
        public bool IsAuthenticated => throw Touched();
        public ActorIdentity Identity => throw Touched();
        public AuthorizingAssignment? Authority => throw Touched();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task<bool> ExistsActiveHumanWithEmailAsync(EmailAddress email, CancellationToken cancellationToken) => throw Touched();
        public Task<bool> ExistsOtherActiveHumanWithEmailAsync(EmailAddress email, UserId excluding, CancellationToken cancellationToken) => throw Touched();
        public Task AddAsync(User user, CancellationToken cancellationToken) => throw Touched();
        public Task<User?> FindAsync(UserId userId, CancellationToken cancellationToken) => throw Touched();
        public Task<Role?> FindAsync(RoleId roleId, CancellationToken cancellationToken) => throw Touched();
        public Task<Role?> FindTrackedAsync(RoleId roleId, CancellationToken cancellationToken) => throw Touched();
        public Task<bool> HasActiveAgentAssignmentAsync(RoleId roleId, DateTimeOffset at, CancellationToken cancellationToken) => throw Touched();
        public Task<RolePermission?> FindLiveAsync(RoleId roleId, PermissionId permissionId, CancellationToken cancellationToken) => throw Touched();
        public Task<RolePermission?> FindTrackedAsync(RolePermissionId rolePermissionId, CancellationToken cancellationToken) => throw Touched();
        public Task AddAsync(RolePermission grant, CancellationToken cancellationToken) => throw Touched();
        public Task<PermissionFacts?> FindAsync(PermissionId permissionId, CancellationToken cancellationToken) => throw Touched();
        public Task<bool> ExistsWithCodeAsync(string code, CancellationToken cancellationToken) => throw Touched();
        public Task AddAsync(Role role, CancellationToken cancellationToken) => throw Touched();
        public Task AddAsync(UserRole assignment, CancellationToken cancellationToken) => throw Touched();
        public Task<UserRole?> FindAsync(UserRoleId assignmentId, CancellationToken cancellationToken) => throw Touched();
        public AuditEventDeclaration Emit(string code, int version) => throw Touched();

        private static InvalidOperationException Touched() => new("A collaborator was touched for a blank reason.");
    }
}
