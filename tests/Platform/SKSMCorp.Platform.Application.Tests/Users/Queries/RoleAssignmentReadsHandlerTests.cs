using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Queries.GrantableRoles;
using SKSMCorp.Platform.Application.Users.Queries.RoleAssignments;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace SKSMCorp.Platform.Application.Tests.Users.Queries;

/// <summary>
/// AUT-Q2 and the grantable-role list: what the handlers own (docs/requirements.md,
/// "AUT-Q2"). A query has no pipeline, so each handler authenticates, then
/// authorises role.read, and only then reads — and every refusal is asserted to
/// happen BEFORE the reader is touched.
///
/// AUT-Q2's state is derived by the handler at the clock's now, from the stored
/// period and revocation, never read from storage: the reader's records carry
/// no state at all.
/// </summary>
public sealed class RoleAssignmentReadsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    // ================================================================ AUT-Q2

    [Fact]
    public void AUT_Q2_requires_role_read()
    {
        var declaration = UserRoleAssignmentsQuery.Authorization;

        Assert.True(declaration.IsRequired);
        Assert.Equal("role.read", declaration.PermissionCode);
    }

    [Fact]
    public void AUT_Q2_is_registered_with_its_handler()
        => AssertRegistered<UserRoleAssignmentsQuery, UserRoleAssignmentsResult, UserRoleAssignmentsQueryHandler>();

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_before_authorization_or_any_read()
    {
        var harness = new AssignmentsHarness { Authenticated = false };

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => harness.HandleAsync(includeInactive: true));

        Assert.Empty(harness.Authorization.Requests);
        Assert.Equal(0, harness.Reader.Calls);
    }

    [Fact]
    public async Task A_caller_without_role_read_is_refused_before_any_read()
    {
        var harness = new AssignmentsHarness { Allowed = false };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => harness.HandleAsync(includeInactive: true));

        Assert.Equal("role.read", Assert.Single(harness.Authorization.Requests).PermissionCode);
        Assert.Equal(0, harness.Reader.Calls);
    }

    [Fact]
    public async Task An_unknown_user_is_refused()
    {
        var harness = new AssignmentsHarness { UnknownUser = true };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => harness.HandleAsync(includeInactive: true));
    }

    /// <summary>Q3 — derived at the clock's now, revocation winning.</summary>
    [Fact]
    public async Task State_is_derived_at_the_clocks_now()
    {
        var harness = new AssignmentsHarness
        {
            Records =
            [
                Record("active", Now.AddDays(-1), null, null),
                Record("future", Now.AddDays(1), null, null),
                Record("ended", Now.AddDays(-10), Now.AddDays(-2), null),
                Record("cancelled", Now.AddDays(5), Now.AddDays(5), Now.AddDays(-1)),
            ],
        };

        var result = await harness.HandleAsync(includeInactive: true);

        Assert.Equal(
            new Dictionary<string, RoleAssignmentState>
            {
                ["active"] = RoleAssignmentState.Active,
                ["future"] = RoleAssignmentState.Future,
                ["ended"] = RoleAssignmentState.Ended,
                ["cancelled"] = RoleAssignmentState.Revoked,
            },
            result.Assignments.ToDictionary(x => x.RoleName, x => x.State));
    }

    /// <summary>Q4 — history is opt-in.</summary>
    [Fact]
    public async Task Without_history_only_active_and_future_assignments_are_returned()
    {
        var harness = new AssignmentsHarness
        {
            Records =
            [
                Record("active", Now.AddDays(-1), null, null),
                Record("future", Now.AddDays(1), null, null),
                Record("ended", Now.AddDays(-10), Now.AddDays(-2), null),
                Record("revoked", Now.AddDays(-10), Now.AddDays(-3), Now.AddDays(-3)),
            ],
        };

        Assert.Equal(
            ["future", "active"],
            (await harness.HandleAsync(includeInactive: false)).Assignments.Select(x => x.RoleName));

        Assert.Equal(4, (await harness.HandleAsync(includeInactive: true)).Assignments.Count);
    }

    /// <summary>Q5 — latest period first, then assignment id.</summary>
    [Fact]
    public async Task Assignments_are_ordered_by_start_descending_then_by_id()
    {
        var sameStart = Now.AddDays(-5);
        var low = new UserRoleId(Guid.Parse("00000000-0000-4000-8000-000000000001"));
        var high = new UserRoleId(Guid.Parse("00000000-0000-4000-8000-000000000002"));

        var harness = new AssignmentsHarness
        {
            Records =
            [
                Record("oldest", Now.AddDays(-30), Now.AddDays(-20), null),
                Record("tie-high", sameStart, null, null, high),
                Record("newest", Now.AddDays(3), null, null),
                Record("tie-low", sameStart, Now.AddDays(1), null, low),
            ],
        };

        Assert.Equal(
            ["newest", "tie-low", "tie-high", "oldest"],
            (await harness.HandleAsync(includeInactive: true)).Assignments.Select(x => x.RoleName));
    }

    /// <summary>Q2 — every stored field reaches the row unchanged.</summary>
    [Fact]
    public async Task A_row_carries_the_stored_fields_unchanged()
    {
        var granter = new AssignmentActor(UserId.New(), "Ada Lovelace");
        var revoker = new AssignmentActor(UserId.New(), "Grace Hopper");

        var record = new UserRoleAssignmentRecord(
            UserRoleId.New(), RoleId.New(), "Access Reviewer",
            Now.AddDays(-10), Now.AddDays(-1), Now.AddDays(-11), granter, "Quarterly review; REQ-7.",
            Now.AddDays(-1), revoker, "Review finished.");

        var row = Assert.Single((await new AssignmentsHarness { Records = [record] }.HandleAsync(true)).Assignments);

        Assert.Equal(record.AssignmentId, row.AssignmentId);
        Assert.Equal(record.RoleId, row.RoleId);
        Assert.Equal("Access Reviewer", row.RoleName);
        Assert.Equal(record.EffectiveFrom, row.EffectiveFrom);
        Assert.Equal(record.EffectiveTo, row.EffectiveTo);
        Assert.Equal(record.AssignedAt, row.AssignedAt);
        Assert.Equal(granter, row.AssignedBy);
        Assert.Equal("Quarterly review; REQ-7.", row.AssignmentReason);
        Assert.Equal(record.RevokedAt, row.RevokedAt);
        Assert.Equal(revoker, row.RevokedBy);
        Assert.Equal("Review finished.", row.RevocationReason);
        Assert.Equal(RoleAssignmentState.Revoked, row.State);
    }

    // ================================================================ roles

    [Fact]
    public void The_grantable_role_list_requires_role_read()
    {
        var declaration = GrantableRolesQuery.Authorization;

        Assert.True(declaration.IsRequired);
        Assert.Equal("role.read", declaration.PermissionCode);
    }

    [Fact]
    public void The_grantable_role_list_is_registered_with_its_handler()
        => AssertRegistered<GrantableRolesQuery, GrantableRolesResult, GrantableRolesQueryHandler>();

    [Fact]
    public async Task The_role_list_refuses_an_unauthenticated_caller_before_reading()
    {
        var reader = new RecordingRoleReader();
        var authorization = new RecordingAuthorizationService(allowed: true);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            new GrantableRolesQueryHandler(new FixedExecutionContext(false), authorization, new FixedClock(), reader)
                .Handle(new GrantableRolesQuery(), CancellationToken.None));

        Assert.Empty(authorization.Requests);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task The_role_list_refuses_a_caller_without_role_read_before_reading()
    {
        var reader = new RecordingRoleReader();
        var authorization = new RecordingAuthorizationService(allowed: false);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            new GrantableRolesQueryHandler(new FixedExecutionContext(true), authorization, new FixedClock(), reader)
                .Handle(new GrantableRolesQuery(), CancellationToken.None));

        Assert.Equal("role.read", Assert.Single(authorization.Requests).PermissionCode);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task The_role_list_returns_what_the_reader_read()
    {
        var reader = new RecordingRoleReader();

        var result = await new GrantableRolesQueryHandler(
                new FixedExecutionContext(true), new RecordingAuthorizationService(true), new FixedClock(), reader)
            .Handle(new GrantableRolesQuery(), CancellationToken.None);

        Assert.Equal(reader.Roles, result.Roles);
    }

    // ================================================================ harness

    private static void AssertRegistered<TQuery, TResult, THandler>()
        where TQuery : IQuery<TResult>
    {
        var services = new ServiceCollection();

        services.AddPlatformApplication();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IQueryHandler<TQuery, TResult>));

        Assert.Equal(typeof(THandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private static UserRoleAssignmentRecord Record(
        string roleName, DateTimeOffset from, DateTimeOffset? to, DateTimeOffset? revokedAt, UserRoleId? id = null)
        => new(
            id ?? UserRoleId.New(), RoleId.New(), roleName, from, to, from.AddDays(-1),
            new AssignmentActor(UserId.New(), "Ada Lovelace"), "Granted.",
            revokedAt, revokedAt is null ? null : new AssignmentActor(UserId.New(), "Ada Lovelace"),
            revokedAt is null ? null : "Revoked.");

    private sealed class AssignmentsHarness
    {
        public bool Authenticated { get; init; } = true;

        public bool Allowed { get; init; } = true;

        public bool UnknownUser { get; init; }

        public IReadOnlyList<UserRoleAssignmentRecord> Records { get; init; } = [];

        public RecordingAuthorizationService Authorization { get; private set; } = null!;

        public RecordingAssignmentReader Reader { get; private set; } = null!;

        public Task<UserRoleAssignmentsResult> HandleAsync(bool includeInactive)
        {
            Authorization = new RecordingAuthorizationService(Allowed);
            Reader = new RecordingAssignmentReader(UnknownUser ? null : Records);

            return new UserRoleAssignmentsQueryHandler(
                    new FixedExecutionContext(Authenticated), Authorization, new FixedClock(), Reader)
                .Handle(new UserRoleAssignmentsQuery(UserId.New(), includeInactive), CancellationToken.None);
        }
    }

    private sealed class RecordingAssignmentReader(IReadOnlyList<UserRoleAssignmentRecord>? records)
        : IUserRoleAssignmentReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<UserRoleAssignmentRecord>?> ReadAsync(UserId userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(records);
        }
    }

    private sealed class RecordingRoleReader : IGrantableRoleReader
    {
        public IReadOnlyList<GrantableRole> Roles { get; } =
        [
            new(RoleId.New(), "Access Reviewer", "Read-only access review."),
            new(RoleId.New(), "User Administrator", null),
        ];

        public int Calls { get; private set; }

        public Task<IReadOnlyList<GrantableRole>> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Roles);
        }
    }

    private sealed class RecordingAuthorizationService(bool allowed) : IAuthorizationService
    {
        public List<AuthorizationRequest> Requests { get; } = [];

        public Task<AuthorizationResult> IsAllowedAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(
                allowed
                    ? AuthorizationResult.Allowed(
                        new AuthorizingAssignment(RoleId.New(), "security-administrator", ScopeType.Global, null, UserRoleId.New()))
                    : AuthorizationResult.Denied);
        }

        public Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
            EffectivePermissionsRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("These reads do not enumerate permissions.");

        // AUT-Q7's view. These doubles stand in for the pipeline's decision,
        // never for the reverse lookup, so calling it here would be a test
        // reaching for something it was not given.
        public Task<IReadOnlyList<PermissionHolder>?> WhoCanDoAsync(
            WhoCanDoRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("This double does not answer AUT-Q7.");
    }

    /// <summary>Caller-dependent members throw when unauthenticated, as the real context's do.</summary>
    private sealed class FixedExecutionContext(bool authenticated) : IExecutionContext
    {
        private readonly UserId _userId = UserId.New();

        public UserId UserId => authenticated ? _userId : throw new InvalidOperationException("No caller.");

        public ActorType ActorType => authenticated ? ActorType.Human : throw new InvalidOperationException("No caller.");

        public bool IsAuthenticated => authenticated;

        public ActorIdentity Identity => TestActorIdentity.Human();

        public AuthorizingAssignment? Authority => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
