using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Notifications;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Application.Users.Commands.CreateUser;
using Ligature.Platform.Application.Users.Commands.ReissueActivationLink;
using Ligature.Platform.Domain.Notifications;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Notifications;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C7 end to end: an established administrator, dispatch through the
/// pipeline, and the rows read back from PostgreSQL.
///
/// THE SUCCESS POSTCONDITION IS ASSERTED WHOLE, not derived from UT4's index:
/// the identity has exactly one open Activation token, it is the one just
/// issued, it is unexpired, and every other Activation token of the identity
/// is invalidated.
///
/// THE REFUSAL INVARIANT: every refusal is seeded with a live prior activation
/// link and compared against a snapshot of every table the command could write
/// (tokens, their invalidation, notifications, audit references and
/// credentials). Where the target is otherwise activatable, the prior link is
/// then USED, which proves it survived in the only sense that matters.
///
/// What that proves, precisely: nothing COMMITS. It does not prove the checks
/// run before the writes, because a rollback leaves the same footprint. The
/// fault-injection case below is what proves a failure AFTER the writes began
/// is also rolled back.
///
/// No concurrency test: the contract states the invariant, which UT4 holds,
/// and deliberately does not specify what a concurrent loser observes.
///
/// Wording is not asserted, but SAMENESS is: every ineligible target gets the
/// one message.
///
/// Its own provisioned database, for ActivationDatabase's reason: activating a
/// subject makes it the actor of audit records.
/// </summary>
public sealed class ReissueActivationLinkIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private const string Reason = "The activation mail never arrived.";
    private const string Password = "an-entirely-new-password-2";
    private const string InjectedFailure = "Injected failure after the writes began.";

    private readonly ActivationDatabase _database;

    public ReissueActivationLinkIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // R1, R2 — one usable link, and it is the new one
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_pending_user_is_left_with_exactly_one_usable_link_and_it_is_the_new_one()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var history = await InsertTokenAsync(subject.IdentityId, invalidated: true);
        var live = await InsertTokenAsync(subject.IdentityId);

        await DispatchAsync(admin.UserId, subject.UserId);

        var issued = await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: [history.Id, live.Id]);

        // The administrator, never SYSTEM_UUID.
        Assert.Equal(admin.UserId.Value, issued.CreatedBy);

        // The effective activation lifetime, as USR-C1 issues it.
        Assert.Equal(
            SecurityBaseline.Current.ActivationTokenLifetime.TotalSeconds,
            issued.LifetimeSeconds,
            precision: 3);
    }

    /// <summary>
    /// UT5 — an EXPIRED but unused link is superseded too: it still holds
    /// UT4's slot, which cannot mention expiry.
    /// </summary>
    [Fact]
    public async Task An_expired_unused_link_is_superseded()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var expired = await InsertTokenAsync(subject.IdentityId, expired: true);

        await DispatchAsync(admin.UserId, subject.UserId);

        await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: [expired.Id]);

        Assert.Equal(1, await CountRecordsAsync("TokenInvalidated", primary: expired.Id));
    }

    /// <summary>
    /// A token of another type is not this command's to touch. A pending user
    /// cannot hold a reset token through any command, but the schema permits
    /// one, and UT5 is per type.
    /// </summary>
    [Fact]
    public async Task A_token_of_another_type_is_not_touched()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var reset = await InsertTokenAsync(subject.IdentityId, TokenType.PasswordReset);

        await DispatchAsync(admin.UserId, subject.UserId);

        var tokens = (await ReadTokensAsync(subject.IdentityId)).ToDictionary(x => x.Id);

        Assert.Null(tokens[reset.Id].InvalidatedAt);
        Assert.Equal(0, await CountRecordsAsync("TokenInvalidated", primary: reset.Id));
    }

    /// <summary>
    /// Zero superseded tokens is valid: a TokenIssued and no TokenInvalidated.
    /// </summary>
    [Fact]
    public async Task A_pending_user_with_no_open_link_gets_one_and_no_invalidation_record()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var history = await InsertTokenAsync(subject.IdentityId, invalidated: true);

        await DispatchAsync(admin.UserId, subject.UserId);

        var issued = await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: [history.Id]);

        Assert.Equal(
            ["TokenIssued"],
            (await ReadOperationAsync(issued.Id)).Select(x => x.EventType));
    }

    /// <summary>
    /// "An external identity beside the local one" does not block it: the rule
    /// is exactly one LOCAL identity, as CRD-C5's is.
    /// </summary>
    [Fact]
    public async Task An_external_identity_beside_the_local_one_does_not_block_the_reissue()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var external = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{external}', '{subject.UserId.Value}', 'Human', 'External', 'EntraId',
                  'external-{external:N}', NULL, 'Active',
                  now() - interval '1 day', '{User.SystemUserId.Value}')
             """);

        await DispatchAsync(admin.UserId, subject.UserId);

        await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: []);
        Assert.Empty(await ReadTokensAsync(external));
    }

    // ------------------------------------------------------------------
    // R3 — the old link fails, the new one works; R5 — the notifications
    // ------------------------------------------------------------------

    /// <summary>
    /// The whole story, through the real creation path: USR-C1 creates the
    /// user and queues their first link; CRD-C7 replaces it. The queued row
    /// for the first link is left exactly as it was, and the eligibility gate
    /// no longer finds its token live, so the pump will close it TokenNotLive
    /// rather than send it. The first link is refused; the new one activates.
    /// </summary>
    [Fact]
    public async Task The_new_link_activates_and_the_one_it_replaced_does_not()
    {
        var admin = await CallerAsync("user-administrator");

        var captured = new List<string>();

        var created = await CreateUserAsync(admin.UserId, captured);
        var first = Assert.Single(captured);
        var firstToken = Assert.Single(await ReadTokensAsync(created.UserIdentityId.Value));
        var firstNotification = Assert.Single(await ReadNotificationsAsync(firstToken.Id));

        await DispatchAsync(admin.UserId, created.UserId, captured: captured);

        var second = captured[1];
        var issued = await AssertExactlyOneUsableLinkAsync(created.UserIdentityId.Value, except: [firstToken.Id]);

        // R5 — one AccountActivation for the new token, to the stored address.
        var notification = Assert.Single(await ReadNotificationsAsync(issued.Id));

        Assert.Equal("AccountActivation", notification.Type);
        Assert.Equal(firstNotification.Recipient, notification.Recipient);

        // The queued row for the superseded link: untouched by CRD-C7 ...
        Assert.Equal(firstNotification, Assert.Single(await ReadNotificationsAsync(firstToken.Id)));

        // ... and the gate will not send it.
        var gate = new NotificationGate(_database.ConnectionString);

        Assert.Equal(
            NotificationEligibility.TokenNotLive,
            await gate.EvaluateAsync(new UserTokenId(firstToken.Id), CancellationToken.None));

        Assert.Equal(
            NotificationEligibility.Eligible,
            await gate.EvaluateAsync(new UserTokenId(issued.Id), CancellationToken.None));

        // R3.
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => ActivateAsync(first));

        await ActivateAsync(second);

        Assert.True(await HasCredentialAsync(created.UserIdentityId.Value));
    }

    // ------------------------------------------------------------------
    // R4 — the audit records, exactly, in order
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_operation_writes_TokenInvalidated_then_TokenIssued_and_nothing_else()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var prior = await InsertTokenAsync(subject.IdentityId);

        var captured = new List<string>();

        await DispatchAsync(admin.UserId, subject.UserId, captured: captured);

        var issued = await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: [prior.Id]);

        var records = await ReadOperationAsync(issued.Id);

        // The order, not merely the set.
        Assert.Equal(["TokenInvalidated", "TokenIssued"], records.Select(x => x.EventType));

        var invalidated = records[0];
        var issuance = records[1];

        Assert.Equal(("Token", prior.Id), (invalidated.EntityType, invalidated.EntityId));
        Assert.Equal(
            [("Identity", subject.IdentityId, "Target"), ("Token", issued.Id, "SupersededBy")],
            await ReadRefsAsync(invalidated.AuditId));
        Assert.Equal("Activation", invalidated.PayloadTokenType);
        Assert.Equal("Superseded", invalidated.PayloadReason);
        Assert.Null(invalidated.Reason);

        Assert.Equal(("Token", issued.Id), (issuance.EntityType, issuance.EntityId));
        Assert.Equal(
            [("Identity", subject.IdentityId, "Target"), ("User", subject.UserId.Value, "Subject")],
            await ReadRefsAsync(issuance.AuditId));
        Assert.Equal("Activation", issuance.PayloadTokenType);

        // The administrator's reason travels on TokenIssued.
        Assert.Equal(Reason, issuance.Reason);

        foreach (var record in records)
        {
            // The administrator authorised this; nothing is AsSystem.
            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(admin.UserId.Value, record.ActorUserId);

            // R11 — NEVER the token, its secret or its hash.
            Assert.DoesNotContain(issued.Hash, record.Payload ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(prior.Hash, record.Payload ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(Assert.Single(captured), record.Payload ?? "", StringComparison.Ordinal);
        }

        // Nothing else names the subject: no UserCreated, no IdentityCreated,
        // no reissue event.
        Assert.Equal(2, await CountRecordsNamingAsync(subject));
    }

    /// <summary>
    /// UT4 allows one open activation token per identity, so a single reissue
    /// can only ever supersede one. Several are superseded across successive
    /// reissues, and each gets its own TokenInvalidated naming its own
    /// successor.
    /// </summary>
    [Fact]
    public async Task Each_superseded_link_gets_one_invalidation_naming_its_successor()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var prior = await InsertTokenAsync(subject.IdentityId);

        await DispatchAsync(admin.UserId, subject.UserId);

        var firstReissue = await AssertExactlyOneUsableLinkAsync(subject.IdentityId, except: [prior.Id]);

        await DispatchAsync(admin.UserId, subject.UserId);

        var secondReissue = await AssertExactlyOneUsableLinkAsync(
            subject.IdentityId, except: [prior.Id, firstReissue.Id]);

        Assert.Equal(1, await CountRecordsAsync("TokenInvalidated", primary: prior.Id));
        Assert.Equal(1, await CountRecordsAsync("TokenInvalidated", primary: firstReissue.Id));

        Assert.Equal(
            firstReissue.Id,
            await SupersededByAsync(prior.Id));

        Assert.Equal(
            secondReissue.Id,
            await SupersededByAsync(firstReissue.Id));

        Assert.Equal(2, await CountRecordsAsync("TokenIssued", subjectUser: subject.UserId.Value));
    }

    // ------------------------------------------------------------------
    // R6, R7 — ineligible targets
    // ------------------------------------------------------------------

    /// <summary>
    /// HasCredential is R6, and what keeps this from becoming a second reset
    /// path: a user who has activated is CRD-C5's. Every other scenario breaks
    /// exactly one other eligibility rule (R7).
    /// </summary>
    [Theory]
    [InlineData(nameof(Scenario.HasCredential))]
    [InlineData(nameof(Scenario.InactiveUser))]
    [InlineData(nameof(Scenario.InactiveIdentity))]
    [InlineData(nameof(Scenario.NoLocalIdentity))]
    [InlineData(nameof(Scenario.TwoLocalIdentities))]
    [InlineData(nameof(Scenario.NoEmail))]
    [InlineData(nameof(Scenario.AgentUser))]
    public async Task An_ineligible_target_is_refused_and_nothing_changes(string scenario)
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync(Enum.Parse<Scenario>(scenario));

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId),
            priorLinkStillActivates: false);
    }

    [Fact]
    public async Task Every_ineligible_target_gets_the_same_message()
    {
        var admin = await CallerAsync("user-administrator");

        var messages = new List<string>();

        foreach (var scenario in Enum.GetValues<Scenario>().Where(x => x != Scenario.Eligible))
        {
            var subject = await SeedAsync(scenario);

            var refusal = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => DispatchAsync(admin.UserId, subject.UserId));

            messages.Add(refusal.Message);
        }

        messages.Add((await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, UserId.New()))).Message);

        messages.Add((await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, User.SystemUserId))).Message);

        Assert.Single(messages.Distinct());
    }

    [Fact]
    public async Task An_unknown_user_is_refused()
    {
        var admin = await CallerAsync("user-administrator");

        var tokensBefore = await ScalarAsync("SELECT count(*) FROM user_token");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, UserId.New()));

        Assert.Equal(tokensBefore, await ScalarAsync("SELECT count(*) FROM user_token"));
    }

    /// <summary>
    /// The System actor cannot authenticate (UI8) and is the one non-human
    /// account that always exists.
    /// </summary>
    [Fact]
    public async Task The_system_actor_is_refused()
    {
        var admin = await CallerAsync("user-administrator");

        var tokensBefore = await ScalarAsync("SELECT count(*) FROM user_token");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(admin.UserId, User.SystemUserId));

        Assert.Equal(tokensBefore, await ScalarAsync("SELECT count(*) FROM user_token"));
    }

    // ------------------------------------------------------------------
    // R8 — the reason
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_reason_is_refused_and_the_prior_link_still_activates(string reason)
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId, reason),
            priorLinkStillActivates: true);
    }

    // ------------------------------------------------------------------
    // R9 — callers who may not
    // ------------------------------------------------------------------

    /// <summary>
    /// access-reviewer holds user.read but not user.create, so this proves the
    /// SPECIFIC permission is checked, not merely that a user role is held.
    /// </summary>
    [Fact]
    public async Task A_caller_holding_a_different_user_permission_is_refused()
    {
        var reviewer = await CallerAsync("access-reviewer");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(reviewer.UserId, subject.UserId),
            priorLinkStillActivates: true);
    }

    [Fact]
    public async Task A_caller_holding_no_role_is_refused()
    {
        var caller = await CallerAsync(null);
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(caller.UserId, subject.UserId),
            priorLinkStillActivates: true);
    }

    /// <summary>
    /// Human-actor only. The caller holds the permission, so the only thing
    /// that differs from the successful case is the actor type.
    /// </summary>
    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        await AssertRefusedAsync(
            subject,
            () => DispatchAsync(admin.UserId, subject.UserId, actorType: ActorType.Agent),
            priorLinkStillActivates: true);
    }

    // ------------------------------------------------------------------
    // R10 — a failure after the writes began
    // ------------------------------------------------------------------

    /// <summary>
    /// The notification is declared LAST, after the prior link has been
    /// invalidated and the new token inserted inside the transaction. A
    /// failure there must take both back: the prior link stays open and
    /// usable, and nothing is left behind.
    /// </summary>
    [Fact]
    public async Task A_failure_after_the_writes_began_leaves_the_prior_link_usable()
    {
        var admin = await CallerAsync("user-administrator");
        var subject = await SeedAsync();

        var prior = await InsertTokenAsync(subject.IdentityId);
        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DispatchAsync(admin.UserId, subject.UserId, failNotification: true));

        // The INJECTED failure, and nothing else: the handler reached the
        // notification, which it declares after its writes.
        Assert.Equal(InjectedFailure, failure.Message);

        Assert.Equal(before, await FootprintAsync(subject));

        await ActivateAsync(prior.PlainText);

        Assert.True(await HasCredentialAsync(subject.IdentityId));
    }

    // ------------------------------------------------------------------

    private enum Scenario
    {
        Eligible,
        HasCredential,
        InactiveUser,
        InactiveIdentity,
        NoLocalIdentity,
        TwoLocalIdentities,
        NoEmail,
        AgentUser,
    }

    private sealed record Subject(UserId UserId, Guid IdentityId, IReadOnlyList<Guid> IdentityIds);

    private sealed record IssuedToken(Guid Id, string PlainText, string Hash);

    private sealed record TokenRow(
        Guid Id, string TokenType, string Hash, Guid CreatedBy,
        string? UsedAt, string? InvalidatedAt, bool Expired, double LifetimeSeconds);

    private sealed record NotificationRow(string Type, string Recipient, string Status);

    private sealed record RecordRow(
        Guid AuditId, string EventType, string EntityType, Guid? EntityId,
        string OriginKind, Guid? ActorUserId, string? Reason, string? Payload,
        string? PayloadTokenType, string? PayloadReason);

    /// <summary>Everything CRD-C7 could write, for one subject.</summary>
    private sealed record Footprint(
        long Tokens, long Open, long Invalidated, long Notifications, long AuditRefs, long Credentials);

    /// <summary>
    /// The postcondition, whole: exactly one open Activation token, unexpired,
    /// not among <paramref name="except"/> (the tokens that existed before),
    /// and every other Activation token of the identity invalidated or used.
    /// Returns the new token.
    /// </summary>
    private async Task<TokenRow> AssertExactlyOneUsableLinkAsync(Guid identityId, Guid[] except)
    {
        var activation = (await ReadTokensAsync(identityId))
            .Where(x => x.TokenType == "Activation")
            .ToList();

        var open = activation.Where(x => x.UsedAt is null && x.InvalidatedAt is null).ToList();

        var issued = Assert.Single(open);

        Assert.DoesNotContain(issued.Id, except);
        Assert.False(issued.Expired);

        Assert.All(
            activation.Where(x => x.Id != issued.Id),
            x => Assert.True(x.InvalidatedAt is not null || x.UsedAt is not null));

        foreach (var id in except)
            Assert.Contains(activation, x => x.Id == id && x.InvalidatedAt is not null);

        return issued;
    }

    /// <summary>
    /// The refusal, and the proof of it: the command throws the ordinary
    /// business-rule exception and every table it writes is exactly as it was.
    /// The prior live link is what makes a skipped check visible. Where the
    /// target could otherwise activate, the link is then used.
    /// </summary>
    private async Task AssertRefusedAsync(Subject subject, Func<Task> dispatch, bool priorLinkStillActivates)
    {
        var priors = new List<IssuedToken>();

        foreach (var identityId in subject.IdentityIds)
            priors.Add(await InsertTokenAsync(identityId));

        var before = await FootprintAsync(subject);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(dispatch);

        Assert.Equal(before, await FootprintAsync(subject));

        if (priorLinkStillActivates)
        {
            await ActivateAsync(Assert.Single(priors).PlainText);

            Assert.True(await HasCredentialAsync(subject.IdentityId));
        }
    }

    private async Task<Footprint> FootprintAsync(Subject subject)
    {
        var identities = subject.IdentityIds.ToArray();
        var everything = identities.Append(subject.UserId.Value).ToArray();

        return new Footprint(
            await ArrayScalarAsync(
                "SELECT count(*) FROM user_token WHERE user_identity_id = ANY(@ids)", identities),
            await ArrayScalarAsync(
                """
                SELECT count(*) FROM user_token
                WHERE user_identity_id = ANY(@ids) AND used_at IS NULL AND invalidated_at IS NULL
                """, identities),
            await ArrayScalarAsync(
                "SELECT count(*) FROM user_token WHERE user_identity_id = ANY(@ids) AND invalidated_at IS NOT NULL",
                identities),
            await ArrayScalarAsync(
                """
                SELECT count(*) FROM notification n
                JOIN user_token t ON t.id = n.token_id
                WHERE t.user_identity_id = ANY(@ids)
                """, identities),
            await ArrayScalarAsync(
                "SELECT count(*) FROM audit.audit_entity_ref WHERE entity_id = ANY(@ids)", everything),
            await ArrayScalarAsync(
                "SELECT count(*) FROM credential WHERE user_identity_id = ANY(@ids)", identities));
    }

    private async Task<PermanentCaller> CallerAsync(string? roleCode)
        => await PermanentTestCaller.EnsureAsync(
            _database.ConnectionString, $"crd-c7-{roleCode ?? "unprivileged"}", roleCode);

    private async Task DispatchAsync(
        UserId caller,
        UserId target,
        string reason = Reason,
        ActorType actorType = ActorType.Human,
        List<string>? captured = null,
        bool failNotification = false)
    {
        await using var provider = Provider(captured, failNotification);

        using var scope = provider.CreateScope();

        Establish(scope, caller, actorType);

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ReissueActivationLinkCommand, ReissueActivationLinkResult>(
                new ReissueActivationLinkCommand(target, reason),
                CancellationToken.None);
    }

    private async Task<CreateUserResult> CreateUserAsync(UserId caller, List<string> captured)
    {
        await using var provider = Provider(captured, failNotification: false);

        using var scope = provider.CreateScope();

        Establish(scope, caller, ActorType.Human);

        var unique = Guid.NewGuid().ToString("N");

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<CreateUserCommand, CreateUserResult>(
                new CreateUserCommand(
                    "Pending", "Person", $"Pending Person {unique[..8]}",
                    $"pending-{unique}@example.test", $"pending-{unique[..12]}"),
                CancellationToken.None);
    }

    private async Task ActivateAsync(string token)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(token, Password),
                CancellationToken.None);
    }

    /// <summary>
    /// The plaintext's only exit is the notification declaration, and the
    /// administrator never receives it. It is observed at the declaration,
    /// without any production seam — and, for R10, made to fail there.
    /// </summary>
    private ServiceProvider Provider(List<string>? captured, bool failNotification)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        if (captured is not null || failNotification)
        {
            services.AddScoped<INotificationEvents>(
                sp => new CapturingNotificationEvents(
                    sp.GetRequiredService<ScopedNotificationEvents>(), captured ?? [], failNotification));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void Establish(IServiceScope scope, UserId caller, ActorType actorType)
        => scope.ServiceProvider
            .GetRequiredService<IExecutionContextInitializer>()
            .Establish(
                caller,
                actorType,
                actorType == ActorType.Human
                    ? TestActorIdentity.Human()
                    : TestActorIdentity.NonHuman());

    private sealed class CapturingNotificationEvents(
        INotificationEvents inner, List<string> captured, bool fail) : INotificationEvents
    {
        public void Emit(
            NotificationType notificationType,
            UserToken token,
            string recipient,
            string plaintextToken)
        {
            if (fail)
                throw new InvalidOperationException(InjectedFailure);

            inner.Emit(notificationType, token, recipient, plaintextToken);
            captured.Add(plaintextToken);
        }
    }

    /// <summary>
    /// A pending user by default: active human, one active local identity, an
    /// email, and NO credential (inv. 15). Each scenario breaks exactly one of
    /// those.
    /// </summary>
    private async Task<Subject> SeedAsync(Scenario scenario = Scenario.Eligible)
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        // AU11 blocks agent creation in the application only; the schema
        // accepts an agent with a local identity and an email. The actor-type
        // check is the only thing that refuses this target.
        var actorType = scenario == Scenario.AgentUser ? "Agent" : "Human";
        var userActive = scenario != Scenario.InactiveUser;
        var identityActive = scenario != Scenario.InactiveIdentity;
        var email = scenario == Scenario.NoEmail ? null : $"pending-{unique}@example.test";
        var localIdentities = scenario switch
        {
            Scenario.NoLocalIdentity => 0,
            Scenario.TwoLocalIdentities => 2,
            _ => 1,
        };

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  deactivated_at, deactivated_by,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', '{actorType}', 'Pending', 'Target', 'Pending Target',
                  {(email is null ? "NULL" : $"'{email}'")},
                  '{(userActive ? "Active" : "Inactive")}',
                  {(userActive ? "NULL, NULL" : $"now(), '{system}'")},
                  now() - interval '1 day', '{system}', now(), '{system}')
             """);

        var identityIds = new List<Guid>();

        for (var i = 0; i < localIdentities; i++)
        {
            var identityId = Guid.NewGuid();
            identityIds.Add(identityId);

            await ExecuteAsync(
                $"""
                 INSERT INTO user_identity
                     (id, user_id, actor_type, identity_type, identity_provider,
                      subject_id, username, status, deactivated_at, deactivated_by,
                      created_at, created_by)
                 VALUES
                     ('{identityId}', '{userId}', '{actorType}', 'Local', 'Application',
                      '{identityId}', 'pending-{i}-{unique}',
                      '{(identityActive ? "Active" : "Inactive")}',
                      {(identityActive ? "NULL, NULL" : $"now(), '{system}'")},
                      now() - interval '1 day', '{system}')
                 """);

            if (scenario != Scenario.HasCredential)
                continue;

            var current = new PasswordHasher().Hash("the-current-password-1");

            await ExecuteAsync(
                $"""
                 INSERT INTO credential
                     (id, user_identity_id, identity_type, password_hash,
                      password_algorithm, password_changed_at, must_change_password,
                      failed_attempt_count, locked_until, created_at, created_by)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', 'Local', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day', false,
                      0, NULL, now() - interval '1 day', '{system}');

                 INSERT INTO password_history
                     (id, user_identity_id, password_hash, password_algorithm, created_at)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day');
                 """);
        }

        return new Subject(new UserId(userId), identityIds.FirstOrDefault(), identityIds);
    }

    private async Task<IssuedToken> InsertTokenAsync(
        Guid identityId,
        TokenType tokenType = TokenType.Activation,
        bool expired = false,
        bool invalidated = false)
    {
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await ExecuteAsync(
            $"""
             INSERT INTO user_token
                 (id, user_identity_id, token_type, token_hash, expires_at,
                  used_at, invalidated_at, created_at, created_by)
             VALUES
                 ('{tokenId.Value}', '{identityId}', '{tokenType}', '{material.Hash}',
                  {(expired ? "now() - interval '1 minute'" : "now() + interval '1 hour'")},
                  NULL,
                  {(invalidated ? "now() - interval '1 hour'" : "NULL")},
                  now() - interval '2 hours', '{User.SystemUserId.Value}')
             """);

        return new IssuedToken(tokenId.Value, material.PlainText, material.Hash);
    }

    private async Task<IReadOnlyList<TokenRow>> ReadTokensAsync(Guid identityId)
    {
        var rows = new List<TokenRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT id, token_type, token_hash, created_by, used_at::text, invalidated_at::text,
                   expires_at <= now(),
                   EXTRACT(EPOCH FROM (expires_at - created_at))::float8
            FROM user_token WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new TokenRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetDouble(7)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<NotificationRow>> ReadNotificationsAsync(Guid tokenId)
    {
        var rows = new List<NotificationRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT notification_type, recipient, status FROM notification WHERE token_id = @id",
            connection);

        command.Parameters.AddWithValue("id", tokenId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            rows.Add(new NotificationRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        return rows;
    }

    /// <summary>
    /// Every record of the operation that issued <paramref name="issuedTokenId"/>,
    /// in sequence order. The operation is found through its TokenIssued.
    /// </summary>
    private async Task<IReadOnlyList<RecordRow>> ReadOperationAsync(Guid issuedTokenId)
    {
        var rows = new List<RecordRow>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.audit_id, r.event_type, r.entity_type, r.entity_id, r.origin_kind,
                   r.actor_user_id, r.reason, r.payload::text,
                   COALESCE(r.payload ->> 'tokenType', r.payload ->> 'TokenType'),
                   COALESCE(r.payload ->> 'reason', r.payload ->> 'Reason')
            FROM audit.audit_record r
            WHERE r.operation_id = (
                SELECT operation_id FROM audit.audit_record
                WHERE event_type = 'TokenIssued' AND entity_id = @token)
            ORDER BY r.sequence
            """, connection);

        command.Parameters.AddWithValue("token", issuedTokenId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RecordRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        Assert.NotEmpty(rows);

        return rows;
    }

    private async Task<IReadOnlyList<(string EntityType, Guid EntityId, string Role)>> ReadRefsAsync(Guid auditId)
    {
        var rows = new List<(string, Guid, string)>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type, entity_id, ref_role FROM audit.audit_entity_ref
            WHERE audit_id = @id
            ORDER BY entity_type, ref_role
            """, connection);

        command.Parameters.AddWithValue("id", auditId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetGuid(1), reader.GetString(2)));

        return rows;
    }

    private async Task<long> CountRecordsAsync(string eventType, Guid? primary = null, Guid? subjectUser = null)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM audit.audit_record r
            WHERE r.event_type = @type
              AND (@primary IS NULL OR r.entity_id = @primary)
              AND (@subject IS NULL OR EXISTS (
                    SELECT 1 FROM audit.audit_entity_ref e
                    WHERE e.audit_id = r.audit_id AND e.entity_type = 'User'
                      AND e.ref_role = 'Subject' AND e.entity_id = @subject))
            """, connection);

        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.Add(new NpgsqlParameter<Guid?>("primary", primary) { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter<Guid?>("subject", subjectUser) { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>Records naming the subject's user or identity, primary or referenced.</summary>
    private async Task<long> CountRecordsNamingAsync(Subject subject)
    {
        var ids = subject.IdentityIds.Append(subject.UserId.Value).ToArray();

        return await ArrayScalarAsync(
            """
            SELECT count(DISTINCT r.audit_id) FROM audit.audit_record r
            LEFT JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.entity_id = ANY(@ids) OR e.entity_id = ANY(@ids)
            """, ids);
    }

    private async Task<Guid> SupersededByAsync(Guid supersededTokenId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT e.entity_id FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'TokenInvalidated' AND r.entity_id = @id
              AND e.entity_type = 'Token' AND e.ref_role = 'SupersededBy'
            """, connection);

        command.Parameters.AddWithValue("id", supersededTokenId);

        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> HasCredentialAsync(Guid identityId)
        => await ArrayScalarAsync(
            "SELECT count(*) FROM credential WHERE user_identity_id = ANY(@ids)", [identityId]) == 1;

    private async Task<long> ArrayScalarAsync(string sql, Guid[] ids)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.AddWithValue("ids", ids);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
