using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Users.Queries.UserList;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Tests.Users.Queries;

/// <summary>
/// USR-Q1 — what the handler owns, which the HTTP suite cannot isolate.
///
/// A query has no pipeline, so the handler is the only thing standing between
/// a caller and the read: it authenticates, then authorizes, then validates the
/// range, and only then reads. Every refusal here is asserted to happen BEFORE
/// the reader is touched — a refusal that still read the tenant's users would
/// pass a status-code test and leak nothing visible, which is exactly the
/// silent failure docs/architecture.md §11 designs against.
///
/// The paging arithmetic is pinned against the contract's literal numbers (25,
/// 100) rather than constants on the type, so changing a limit requires
/// changing the contract and this test, not one line of code.
/// </summary>
public sealed class UsersQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------ authorization

    [Fact]
    public void The_query_declares_that_it_requires_user_read()
    {
        var declaration = DeclarationOf<UsersQuery>();

        Assert.True(declaration.IsRequired);
        Assert.Equal("user.read", declaration.PermissionCode);
    }

    /// <summary>
    /// Registered through AddQuery, so the classification above is what
    /// start-up verification reads. A handler registered any other way would
    /// refuse start-up (docs/architecture.md §11).
    /// </summary>
    [Fact]
    public void The_query_is_registered_with_its_handler()
    {
        var services = new ServiceCollection();

        services.AddPlatformApplication();

        var descriptor = Assert.Single(
            services, x => x.ServiceType == typeof(IQueryHandler<UsersQuery, UsersResult>));

        Assert.Equal(typeof(UsersQueryHandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_before_authorization_or_any_read()
    {
        var harness = new Harness { Authenticated = false };

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => harness.HandleAsync(new UsersQuery(null, null)));

        Assert.Empty(harness.Authorization.Requests);
        Assert.Empty(harness.Reader.Calls);
    }

    [Fact]
    public async Task A_caller_without_user_read_is_refused_before_any_read()
    {
        var harness = new Harness { Allowed = false };

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => harness.HandleAsync(new UsersQuery(null, null)));

        Assert.Empty(harness.Reader.Calls);
    }

    [Fact]
    public async Task Authorization_asks_for_user_read_for_the_caller_globally_now()
    {
        var harness = new Harness();

        await harness.HandleAsync(new UsersQuery(null, null));

        var request = Assert.Single(harness.Authorization.Requests);

        Assert.Equal(harness.Context.UserId, request.UserId);
        Assert.Equal("user.read", request.PermissionCode);
        Assert.Equal(Now, request.At);
        Assert.Equal("Global", request.ScopeType);
        Assert.Null(request.ScopeId);
    }

    /// <summary>
    /// A refused caller learns nothing about the parameters' validity: the
    /// authorization refusal is what they receive, whatever they sent.
    /// </summary>
    [Fact]
    public async Task An_unauthorized_caller_with_invalid_parameters_is_refused_as_unauthorized()
    {
        var harness = new Harness { Allowed = false };

        var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => harness.HandleAsync(new UsersQuery(0, 1000)));

        var authorized = new Harness();

        var rangeRefusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => authorized.HandleAsync(new UsersQuery(0, 1000)));

        Assert.NotEqual(rangeRefusal.Message, refusal.Message);
        Assert.Empty(harness.Reader.Calls);
    }

    // ------------------------------------------------------ parameters

    [Fact]
    public async Task Omitted_page_and_page_size_mean_page_1_of_25()
    {
        var harness = new Harness();

        var result = await harness.HandleAsync(new UsersQuery(null, null));

        Assert.Equal((0, 26), Assert.Single(harness.Reader.Calls));
        Assert.Equal(1, result.Page);
        Assert.Equal(25, result.PageSize);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(int.MinValue, null)]
    [InlineData(null, 0)]
    [InlineData(null, -5)]
    [InlineData(null, 101)]
    [InlineData(null, int.MaxValue)]
    public async Task An_out_of_range_value_is_refused_and_nothing_is_read(int? page, int? pageSize)
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => harness.HandleAsync(new UsersQuery(page, pageSize)));

        Assert.Empty(harness.Reader.Calls);
    }

    [Theory]
    [InlineData(1, 1, 0, 2)]
    [InlineData(1, 100, 0, 101)]
    [InlineData(3, 10, 20, 11)]
    [InlineData(2, 25, 25, 26)]
    public async Task The_read_is_offset_by_the_pages_before_and_asks_for_one_extra_row(
        int page, int pageSize, int offset, int limit)
    {
        var harness = new Harness();

        var result = await harness.HandleAsync(new UsersQuery(page, pageSize));

        Assert.Equal((offset, limit), Assert.Single(harness.Reader.Calls));
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
    }

    // ------------------------------------------------------ hasMore

    [Fact]
    public async Task The_extra_row_means_another_page_and_is_not_returned()
    {
        var rows = Rows(11);
        var harness = new Harness { Rows = rows };

        var result = await harness.HandleAsync(new UsersQuery(1, 10));

        Assert.True(result.HasMore);
        Assert.Equal(rows.Take(10), result.Users);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(3)]
    [InlineData(0)]
    public async Task Without_the_extra_row_there_is_no_further_page(int returned)
    {
        var rows = Rows(returned);
        var harness = new Harness { Rows = rows };

        var result = await harness.HandleAsync(new UsersQuery(1, 10));

        Assert.False(result.HasMore);
        Assert.Equal(rows, result.Users);
    }

    // ------------------------------------------------------ offset representation

    /// <summary>
    /// The reader's offset is an int. (page − 1) × pageSize past its range is
    /// an empty page with no further page, and the reader is not asked — the
    /// alternative is an overflowed, possibly negative or wrapped offset
    /// reaching the database.
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(1073741825, 2)]
    public async Task An_offset_the_reader_cannot_represent_is_an_empty_last_page_without_reading(
        int page, int pageSize)
    {
        var harness = new Harness();

        var result = await harness.HandleAsync(new UsersQuery(page, pageSize));

        Assert.Empty(result.Users);
        Assert.False(result.HasMore);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Empty(harness.Reader.Calls);
    }

    [Fact]
    public async Task The_largest_representable_offset_is_still_read()
    {
        var harness = new Harness();

        // (1073741824 − 1) × 2 = 2147483646, one below int.MaxValue.
        await harness.HandleAsync(new UsersQuery(1073741824, 2));

        Assert.Equal((2147483646, 3), Assert.Single(harness.Reader.Calls));
    }

    // ----------------------------------------------------------- harness

    private static QueryAuthorization DeclarationOf<TQuery>()
        where TQuery : IQueryAuthorizationDeclaration
        => TQuery.Authorization;

    private static IReadOnlyList<UserListRow> Rows(int count)
        => Enumerable.Range(0, count)
            .Select(i => new UserListRow(UserId.New(), $"User {i}", $"user{i}@example.test", ActivationPending: i % 2 == 0, Status: UserStatus.Active))
            .ToList();

    private sealed class Harness
    {
        public bool Authenticated { get; init; } = true;

        public bool Allowed { get; init; } = true;

        public IReadOnlyList<UserListRow> Rows { get; init; } = [];

        public FixedExecutionContext Context { get; private set; } = null!;

        public RecordingAuthorizationService Authorization { get; private set; } = null!;

        public RecordingReader Reader { get; private set; } = null!;

        public Task<UsersResult> HandleAsync(UsersQuery query)
        {
            Context = new FixedExecutionContext(Authenticated);
            Authorization ??= new RecordingAuthorizationService(Allowed);
            Reader ??= new RecordingReader(Rows);

            var handler = new UsersQueryHandler(
                Context, Authorization, new FixedClock(), Reader);

            return handler.Handle(query, CancellationToken.None);
        }
    }

    private sealed class RecordingReader : IUserListReader
    {
        private readonly IReadOnlyList<UserListRow> _rows;

        public RecordingReader(IReadOnlyList<UserListRow> rows) => _rows = rows;

        public List<(int Offset, int Limit)> Calls { get; } = [];

        public Task<IReadOnlyList<UserListRow>> ReadAsync(
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            Calls.Add((offset, limit));

            return Task.FromResult(_rows);
        }
    }

    private sealed class RecordingAuthorizationService : IAuthorizationService
    {
        private readonly bool _allowed;

        public RecordingAuthorizationService(bool allowed) => _allowed = allowed;

        public List<AuthorizationRequest> Requests { get; } = [];

        public Task<AuthorizationResult> IsAllowedAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(
                _allowed
                    ? AuthorizationResult.Allowed(
                        new AuthorizingAssignment(
                            RoleId.New(), "user-administrator", ScopeType.Global, null, UserRoleId.New()))
                    : AuthorizationResult.Denied);
        }

        public Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
            EffectivePermissionsRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("USR-Q1 does not enumerate permissions.");
    }

    /// <summary>
    /// UserId and ActorType throw when unauthenticated, as the real
    /// ScopedExecutionContext does, so a handler that skipped the
    /// authentication check cannot quietly read a default caller.
    /// </summary>
    private sealed class FixedExecutionContext : IExecutionContext
    {
        private readonly UserId _userId = UserId.New();

        public FixedExecutionContext(bool authenticated) => IsAuthenticated = authenticated;

        public UserId UserId => IsAuthenticated
            ? _userId
            : throw new InvalidOperationException("No authenticated caller.");

        public ActorType ActorType => IsAuthenticated
            ? ActorType.Human
            : throw new InvalidOperationException("No authenticated caller.");

        public bool IsAuthenticated { get; }

        public ActorIdentity Identity => TestActorIdentity.Human();

        public AuthorizingAssignment? Authority => null;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
