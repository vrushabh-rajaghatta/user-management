using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.Platform.Application.Users.Queries.Me;
using SKSMCorp.Platform.Domain.Users;
using SKSMCorp.SharedKernel.Abstractions;
using SKSMCorp.SharedKernel.Exceptions;

namespace SKSMCorp.Platform.Application.Tests.Users.Queries;

/// <summary>
/// B6-B. The handler assembles /me from three sources that must not be
/// conflated, and these tests pin two properties the HTTP suite cannot reach.
///
/// D5 — the identity in the response comes from a DEDICATED identity read, not
/// from IExecutionContext.Identity. That is a boundary argument, not a
/// staleness one: ActorIdentity is request-scoped and therefore current, but it
/// exists to serve Audit, carries no UserIdentityId, and an audit-driven change
/// to it must not silently change an API contract.
///
/// D4 — the handler is NOT a second session validator. Caller establishment is
/// the authentication boundary. The handler READS its decision (was a caller
/// established for this request?) and never makes one of its own (is this
/// session valid?).
/// </summary>
public sealed class MeQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserSessionId Session = UserSessionId.New();

    private static readonly UserIdentityId Identity = UserIdentityId.New();

    [Fact]
    public async Task The_identity_comes_from_the_identity_read()
    {
        // The reader is the ONLY source of these values here. If the handler
        // assembled them from anywhere else, the names below could not appear.
        var result = await HandleAsync(
            Caller() with { Username = "ada.lovelace", DisplayName = "Ada, Countess of Lovelace" });

        Assert.Equal(Identity, result.Identity.UserIdentityId);
        Assert.Equal("ada.lovelace", result.Identity.Username);
        Assert.Equal("Ada, Countess of Lovelace", result.Identity.DisplayName);
    }

    [Fact]
    public async Task The_effective_permissions_come_from_the_authorization_service()
    {
        var result = await HandleAsync(
            Caller(),
            permissions: [new EffectivePermission("user.create", "Global", null)]);

        var held = Assert.Single(result.Permissions);

        Assert.Equal("user.create", held.Code);
        Assert.Equal("Global", held.ScopeType);
        Assert.Null(held.ScopeId);
    }

    /// <summary>
    /// The idle expiry is reported, not enforced. It is computed from the
    /// session's last activity and the effective policy, and is a conservative
    /// LOWER BOUND: activity writes are throttled, so the server may accept a
    /// request after the moment this names.
    /// </summary>
    [Fact]
    public async Task The_session_timing_is_reported_from_the_session_and_the_policy()
    {
        var result = await HandleAsync(
            Caller() with
            {
                SessionExpiresAt = Now.AddHours(11),
                SessionLastActivityAt = Now.AddMinutes(-5),
            });

        Assert.Equal(Now.AddHours(11), result.Session.ExpiresAt);
        Assert.Equal(Now.AddMinutes(-5) + IdleTimeout, result.Session.IdleExpiresAt);
    }

    /// <summary>
    /// The boundary's decision, read FIRST. A validly signed carrier for a
    /// session the boundary refused (ended, revoked, missing, or whose user or
    /// identity is inactive) still reaches the handler with its SessionId, but
    /// with no established caller. That is the ordinary authentication refusal,
    /// before the session, the permissions or the policy is read: nothing about
    /// a session the boundary refused is looked up, let alone described.
    /// </summary>
    [Fact]
    public async Task A_request_with_no_established_caller_is_refused_before_anything_is_read()
    {
        var reader = new FixedCallerReader(Caller());
        var authorization = new FixedAuthorizationService([]);
        var policy = new FixedPolicyResolver();

        var handler = new MeQueryHandler(
            new FixedExecutionContext(authenticated: false),
            reader,
            authorization,
            policy,
            new FixedClock());

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => handler.Handle(new MeQuery(Session), CancellationToken.None));

        Assert.Equal(0, reader.Calls);
        Assert.Equal(0, authorization.Calls);
        Assert.Equal(0, policy.Calls);
    }

    /// <summary>
    /// D4, and the reason it is a unit test: over HTTP the middleware records
    /// activity immediately after establishing the caller, so a stale session
    /// is refreshed before any handler could see it. Here the handler is handed
    /// a session whose absolute expiry has PASSED and whose idle window closed
    /// long ago.
    ///
    /// It must still answer. Caller establishment is the authentication
    /// boundary and has already accepted this request; a handler that refused
    /// here would be a second authentication authority, which is exactly what
    /// that boundary exists to prevent. The refusal above is not that: it reads
    /// the boundary's answer, and this test keeps it from growing into one.
    /// </summary>
    [Fact]
    public async Task The_handler_does_not_re_decide_whether_the_session_is_valid()
    {
        var result = await HandleAsync(
            Caller() with
            {
                SessionExpiresAt = Now.AddHours(-1),
                SessionLastActivityAt = Now.AddDays(-7),
            });

        Assert.Equal(Identity, result.Identity.UserIdentityId);
        Assert.Equal(Now.AddHours(-1), result.Session.ExpiresAt);
    }

    // ----------------------------------------------------------- harness

    // The real baseline, so the arithmetic under test is the deployment's.
    private static readonly TimeSpan IdleTimeout =
        SecurityBaseline.Current.SessionIdleTimeout;

    private static AuthenticatedCaller Caller()
        => new(Identity, "ada", "Ada Lovelace", Now.AddHours(12), Now);

    private static async Task<MeResult> HandleAsync(
        AuthenticatedCaller caller,
        IReadOnlyList<EffectivePermission>? permissions = null)
    {
        var handler = new MeQueryHandler(
            new FixedExecutionContext(),
            new FixedCallerReader(caller),
            new FixedAuthorizationService(permissions ?? []),
            new FixedPolicyResolver(),
            new FixedClock());

        return await handler.Handle(new MeQuery(Session), CancellationToken.None);
    }

    private sealed class FixedCallerReader : IAuthenticatedCallerReader
    {
        private readonly AuthenticatedCaller _caller;

        public FixedCallerReader(AuthenticatedCaller caller) => _caller = caller;

        public int Calls { get; private set; }

        public Task<AuthenticatedCaller?> ReadAsync(
            UserSessionId sessionId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<AuthenticatedCaller?>(_caller);
        }
    }

    private sealed class FixedAuthorizationService : IAuthorizationService
    {
        private readonly IReadOnlyList<EffectivePermission> _permissions;

        public FixedAuthorizationService(IReadOnlyList<EffectivePermission> permissions)
            => _permissions = permissions;

        public int Calls { get; private set; }

        public Task<AuthorizationResult> IsAllowedAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(AuthorizationResult.Denied);
        }

        public Task<IReadOnlyList<EffectivePermission>> EnumerateAsync(
            EffectivePermissionsRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_permissions);
        }

        // AUT-Q7's view. /me asks what the CALLER holds; the reverse lookup is
        // a different question, and this double answers only the first.
        public Task<IReadOnlyList<PermissionHolder>> WhoCanDoAsync(
            WhoCanDoRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("This double does not answer AUT-Q7.");
    }

    private sealed class FixedPolicyResolver : ISecurityPolicyResolver
    {
        public int Calls { get; private set; }

        public Task<SecurityPolicySettings> GetEffectiveSettingsAsync(
            DateTimeOffset at,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(SecurityBaseline.Current);
        }
    }

    /// <summary>
    /// Supplies the caller's UserId, which the handler needs to enumerate
    /// permissions. Identity is present because the interface requires it and
    /// deliberately unused by the handler (D5).
    ///
    /// Unauthenticated, it behaves as ScopedExecutionContext does: every
    /// caller-dependent member throws, so a handler that skipped the check
    /// could not pass by reading a placeholder.
    /// </summary>
    private sealed class FixedExecutionContext(bool authenticated = true) : IExecutionContext
    {
        private readonly UserId _userId = UserId.New();

        public UserId UserId => authenticated ? _userId : throw Unestablished();

        public ActorType ActorType => authenticated ? ActorType.Human : throw Unestablished();

        public bool IsAuthenticated => authenticated;

        public ActorIdentity Identity => authenticated ? TestActorIdentity.Human() : throw Unestablished();

        public AuthorizingAssignment? Authority => null;

        private static InvalidOperationException Unestablished()
            => new("No authenticated caller has been established for this scope.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
