using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Application.Users.Commands.ActivateAccount;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Database;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C1 end to end: a real token, the real adapters, dispatch through the
/// pipeline, and the rows read back from PostgreSQL.
///
/// The caller is no longer nobody. Consuming the token establishes the bearer
/// as the actor (AUD-D28), which is what lets the three records this command
/// now writes carry an authenticated origin.
///
/// Runs against its own provisioned database rather than the shared one, for
/// the reason ActivationDatabase explains: an activated user is permanently
/// referenced by the trail and cannot be cleaned up afterwards.
/// </summary>
public sealed class ActivateAccountIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private readonly ActivationDatabase _database;

    public ActivateAccountIntegrationTests(ActivationDatabase database)
        => _database = database;

    /// <summary>
    /// The SEEDING clock, and deliberately not a literal.
    ///
    /// It was <c>new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero)</c>, which made every
    /// test in this class fail from 2026-09-11T09:00Z onwards — not flakily,
    /// permanently. SeedAsync issues its activation token with
    /// <c>expiresAt = Now + ActivationTokenLifetime</c>, 72 hours, while
    /// BuildProvider registers the real SystemClock through
    /// AddPlatformPersistence. The FixedClock below reaches CreateContext's
    /// provenance stamping and nothing else, so the handler read real time,
    /// found the token expired, and refused it.
    ///
    /// The pipeline must keep the real clock: this is an integration test, and
    /// pinning the dispatcher to a past instant would move audit timestamps and
    /// session lifetimes with it. So the seed moves instead. Every row this
    /// class writes is now relative to the run, which is what the literal was
    /// approximating before the calendar caught up with it.
    /// </summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private const string GoodPassword = "a-sufficiently-long-password";

    [Fact]
    public async Task A_valid_token_creates_the_credential_and_its_history()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var result = await dispatcher
                .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                    CancellationToken.None);

            Assert.Equal(fixture.IdentityId, result.UserIdentityId);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.Equal("Local", credential.IdentityType);
            Assert.False(credential.MustChangePassword);
            Assert.Equal(0, credential.FailedAttemptCount);
            Assert.Null(credential.LockedUntil);

            // Nobody is authenticated, so the row is the System actor's.
            Assert.Equal(User.SystemUserId.Value, credential.CreatedBy);

            // CR5 — history written in the same transaction, or the reuse
            // check has a permanent hole from the very first password.
            var history = await ReadHistoryAsync(fixture.IdentityId);

            Assert.Equal(credential.PasswordHash, history.PasswordHash);
            Assert.Equal(credential.PasswordAlgorithm, history.PasswordAlgorithm);

            // The token is spent.
            Assert.NotNull(await ReadTokenUsedAtAsync(fixture.TokenId));
        });
    }

    /// <summary>
    /// The catalogue's named failure mode: two simultaneous clicks. The second
    /// must lose, and it must lose on the conditional UPDATE rather than on a
    /// prior read.
    /// </summary>
    [Fact]
    public async Task A_token_cannot_be_consumed_twice()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            var failure = await Assert
                .ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                            new ActivateAccountCommand(
                                fixture.TokenPlainText, "another-long-password"),
                            CancellationToken.None));

            Assert.Equal("The activation token is not valid.", failure.Message);

            // And the replay left no second credential behind.
            Assert.Equal(1, await CountCredentialsAsync(fixture.IdentityId));
            Assert.Equal(1, await CountHistoryAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// The nasty case this story exists to prevent. A password below the policy
    /// floor is rejected AFTER the token has been consumed inside the
    /// transaction — so the consumption must roll back with it, or the user is
    /// permanently unable to activate because of a typo.
    /// </summary>
    [Fact]
    public async Task A_rejected_password_leaves_the_token_usable()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher
                    .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                        new ActivateAccountCommand(fixture.TokenPlainText, "short"),
                        CancellationToken.None));

            Assert.Null(await ReadTokenUsedAtAsync(fixture.TokenId));
            Assert.Equal(0, await CountCredentialsAsync(fixture.IdentityId));
            Assert.Equal(0, await CountHistoryAsync(fixture.IdentityId));

            // The same token still works, which is the whole point.
            var result = await dispatcher
                .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                    CancellationToken.None);

            Assert.Equal(fixture.IdentityId, result.UserIdentityId);
            Assert.NotNull(await ReadTokenUsedAtAsync(fixture.TokenId));
        });
    }

    [Fact]
    public async Task A_password_below_the_effective_floor_is_refused()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var floor = SecurityBaseline.Current.PasswordMinLength;

            var failure = await Assert
                .ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                            new ActivateAccountCommand(
                                fixture.TokenPlainText, new string('x', floor - 1)),
                            CancellationToken.None));

            Assert.Contains(floor.ToString(), failure.Message);

            // Exactly at the floor is acceptable.
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(
                    fixture.TokenPlainText, new string('x', floor)),
                CancellationToken.None);

            Assert.Equal(1, await CountCredentialsAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// Six different reasons, one message. Anything else turns the activation
    /// endpoint into a token-state oracle.
    /// </summary>
    [Fact]
    public async Task Every_invalid_token_gives_the_same_answer()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            // ck_user_token_expires_after_created still applies: an expired
            // token is one issued before it lapsed, not one that never made
            // sense.
            //
            // On an identity of its own, because UT4 permits at most one OPEN
            // token per (identity, type) and expiry does not close a token —
            // an expired-but-unused token still occupies the slot the
            // fixture's own token already holds. Which identity it belongs to
            // is irrelevant to what this test asserts.
            var expired = (await IssueTokenAsync(
                await SeedIdentityAsync(fixture.UserId),
                expiresAt: Now.AddDays(-1),
                createdAt: Now.AddDays(-2))).PlainText;

            // No extra identity needed: an invalidated token is closed, so it
            // occupies no UT4 slot.
            var invalidated = (await IssueTokenAsync(
                fixture.IdentityId, invalidatedAt: Now)).PlainText;

            var candidates = new[]
            {
                ("unknown id", $"{Guid.NewGuid()}.{fixture.Secret}"),
                ("wrong secret", $"{fixture.TokenId.Value}.wrong-secret-value"),
                ("expired", expired),
                ("invalidated", invalidated),
                ("malformed", "not-a-token"),
                ("empty secret", $"{fixture.TokenId.Value}."),
            };

            foreach (var (reason, token) in candidates)
            {
                var failure = await Assert
                    .ThrowsAsync<BusinessRuleViolationException>(
                        () => dispatcher
                            .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                                new ActivateAccountCommand(token, GoodPassword),
                                CancellationToken.None));

                Assert.Equal(
                    "The activation token is not valid.", failure.Message);

                Assert.Equal(
                    0, await CountCredentialsAsync(fixture.IdentityId));
            }
        });
    }

    [Fact]
    public async Task The_plaintext_password_is_never_persisted()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            foreach (var dump in new[]
            {
                await DumpRowAsync("credential", fixture.IdentityId),
                await DumpRowAsync("password_history", fixture.IdentityId),
            })
            {
                Assert.DoesNotContain(
                    GoodPassword, dump, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    // ------------------------------------------------------------ the trail

    /// <summary>
    /// CRD-C1's three records. The bearer proved possession of a token, which
    /// is the whole of their authentication, and the records that follow are
    /// attributed to them: the trail's answer to "who set this password" is
    /// the person who set it, not the System actor that owns the provenance
    /// columns on the credential row.
    /// </summary>
    [Fact]
    public async Task Activation_records_the_bearers_three_events_under_one_operation()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            var records = await ReadTrailAsync(fixture);

            Assert.Equal(
                ["TokenConsumed", "PasswordSet", "AccountActivated"],
                records.Select(x => x.EventType));

            // AR15 — one command, one operation. AR16 stays empty: these three
            // are members of the same act, not causes of one another.
            Assert.Single(records.Select(x => x.OperationId).Distinct());
            Assert.All(records, x => Assert.Null(x.CausationId));

            Assert.All(records, x =>
            {
                Assert.Equal("Transactional", x.WritePath);
                Assert.Equal("Authenticated", x.OriginKind);
                Assert.Equal(fixture.UserId.Value, x.ActorUserId);
                Assert.Equal("Human", x.ActorType);

                // Nobody granted the bearer anything. Possession of a token is
                // not a role, and AR12's columns say so by staying empty.
                Assert.Null(x.AuthorizingRoleId);
                Assert.Null(x.AuthorizingAssignmentId);
            });

            var consumed = records[0];
            var passwordSet = records[1];
            var activated = records[2];

            Assert.Equal("Token", consumed.EntityType);
            Assert.Equal(fixture.TokenId.Value, consumed.EntityId);

            Assert.Equal("Credential", passwordSet.EntityType);
            Assert.Equal("Identity", activated.EntityType);
            Assert.Equal(fixture.IdentityId.Value, activated.EntityId);

            // Shape None: the fact IS the record.
            Assert.Null(activated.Payload);
            Assert.Null(activated.Before);
            Assert.Null(activated.After);

            Assert.Equal(
                [$"Identity/{fixture.IdentityId.Value}/Target",
                 $"User/{fixture.UserId.Value}/Subject"],
                await ReadReferencesAsync(consumed.AuditId));

            Assert.Equal(
                [$"Identity/{fixture.IdentityId.Value}/Target",
                 $"User/{fixture.UserId.Value}/Subject"],
                await ReadReferencesAsync(passwordSet.AuditId));

            Assert.Equal(
                [$"User/{fixture.UserId.Value}/Subject"],
                await ReadReferencesAsync(activated.AuditId));
        });
    }

    /// <summary>
    /// AUD-D28's chain, asserted end to end: the actor on the records is the
    /// user that owns the identity the CONSUMPTION returned, not one resolved
    /// some other way.
    ///
    /// The fixture seeds a second user with an identity of their own, so a
    /// resolution that wandered — by username, by "the most recent identity",
    /// by anything but the consumed id — would have another candidate to land
    /// on and this assertion would catch it.
    /// </summary>
    [Fact]
    public async Task The_actor_is_the_user_that_owns_the_consumed_identity()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var bystander = await SeedAsync();

            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            var records = await ReadTrailAsync(fixture);

            Assert.NotEmpty(records);

            Assert.All(records, x =>
            {
                Assert.Equal(fixture.UserId.Value, x.ActorUserId);
                Assert.NotEqual(bystander.UserId.Value, x.ActorUserId);
            });

            // And the subject the records name is that same person.
            Assert.All(
                await ReadReferencesAsync(records[0].AuditId),
                x => Assert.DoesNotContain(bystander.UserId.Value.ToString(), x));
        });
    }

    [Fact]
    public async Task A_rejected_password_records_nothing()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText, "short"),
                    CancellationToken.None));

            // The token consumption, the establishment and the three
            // declarations all happened. None of it was written, because the
            // command they belonged to did not commit (invariant 16).
            Assert.Empty(await ReadTrailAsync(fixture));
        });
    }

    /// <summary>
    /// The autonomous path, proved where it matters: the command threw, its
    /// transaction rolled back — the token is deliberately left usable — and
    /// the record of the rejection is still there. A transactional record
    /// would have gone with the rollback, leaving a refused activation with no
    /// trace at all.
    /// </summary>
    [Fact]
    public async Task A_rejected_token_is_recorded_even_though_the_command_rolled_back()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand(fixture.TokenPlainText + "x", GoodPassword),
                    CancellationToken.None));

            // The command wrote nothing: the token is untouched and no
            // credential exists.
            Assert.Null(await ReadTokenUsedAtAsync(fixture.TokenId));
            Assert.Equal(0, await CountCredentialsAsync(fixture.IdentityId));

            var record = Assert.Single(await ReadRejectionsAsync(fixture.TokenId.Value));

            Assert.Equal("Autonomous", record.WritePath);
            Assert.Equal("Anonymous", record.OriginKind);
            Assert.Null(record.ActorUserId);
            Assert.Equal("Token", record.EntityType);
            Assert.Contains("NotUsable", record.Payload);

            // Neither the secret nor the plaintext went anywhere near it.
            Assert.DoesNotContain(fixture.Secret, record.Payload!, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The consumption contract, seen from the command.
    ///
    /// A password-reset token is a perfectly valid token: correct id, correct
    /// secret, unused, unexpired, active subject. Only its TYPE is wrong, and
    /// the plaintext does not carry one — so before UT6 gained that predicate
    /// this activated the account, and because activation inserts a credential
    /// unconditionally the identity came away with two password hashes.
    ///
    /// Unreachable until CRD-C2 issues the first reset token. Closed now so
    /// that story inherits the contract rather than rediscovering it.
    /// </summary>
    [Fact]
    public async Task A_password_reset_token_cannot_activate_an_account()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            var reset = await IssueTokenAsync(
                fixture.IdentityId, tokenType: TokenType.PasswordReset);

            var failure = await Assert
                .ThrowsAsync<BusinessRuleViolationException>(
                    () => dispatcher
                        .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                            new ActivateAccountCommand(reset.PlainText, GoodPassword),
                            CancellationToken.None));

            // The same opaque message as every other refusal.
            Assert.Contains("not valid", failure.Message, StringComparison.OrdinalIgnoreCase);

            // And the point of the predicate living inside the conditional
            // UPDATE: the reset token is still usable for the reset it was
            // issued for. Burning it here would have denied the user the thing
            // they actually asked for.
            Assert.Null(await ReadTokenUsedAtAsync(reset.Id));

            Assert.Equal(0, await CountCredentialsAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// Activation is an authentication event, and the two places that already
    /// ask "is this subject live" — SignInCommandHandler and NotificationGate —
    /// both require the identity AND its user to be active. Activation asked
    /// neither, which left a sharp asymmetry: the notification slice refuses to
    /// SEND the activation mail to a deactivated person, while the endpoint
    /// would have accepted the token if they already held it.
    /// </summary>
    [Theory]
    [InlineData("app_user", false)]
    [InlineData("user_identity", true)]
    public async Task A_deactivated_subject_cannot_activate(
        string table, bool byIdentity)
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await DeactivateAsync(
                table, byIdentity ? fixture.IdentityId.Value : fixture.UserId.Value);

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher
                    .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                        new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                        CancellationToken.None));

            // Unconsumed, which is what makes a deactivation reversible: the
            // mail this person was sent still works once somebody restores
            // their account, and no new token has to be issued to a user who
            // cannot request one.
            Assert.Null(await ReadTokenUsedAtAsync(fixture.TokenId));

            Assert.Equal(0, await CountCredentialsAsync(fixture.IdentityId));
        });
    }

    [Fact]
    public async Task A_reactivated_subject_can_still_use_the_token_they_were_sent()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await DeactivateAsync("app_user", fixture.UserId.Value);

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher
                    .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                        new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                        CancellationToken.None));

            await ReactivateAsync("app_user", fixture.UserId.Value);

            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            Assert.Equal(1, await CountCredentialsAsync(fixture.IdentityId));
        });
    }

    /// <summary>
    /// A token that does not even parse has no id to name, which is why the
    /// catalogue does not require a primary entity id for this event.
    /// </summary>
    [Fact]
    public async Task A_malformed_token_is_recorded_without_one()
    {
        await RunAsync(async (dispatcher, _) =>
        {
            // A malformed rejection names no token, so there is no id to find
            // it by. The class owns this database and runs its tests in order,
            // so "written after this point" identifies it exactly.
            var before = await ReadLastSequenceAsync();

            await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                    new ActivateAccountCommand("not-a-token", GoodPassword),
                    CancellationToken.None));

            var record = Assert.Single(await ReadRejectionsAsync(tokenId: null, after: before));

            Assert.Null(record.EntityId);
            Assert.Contains("Malformed", record.Payload);
        });
    }

    /// <summary>
    /// The frozen failure semantics, on the path where the command FAILS. The
    /// caller broke a rule; that is what explains the request and that is what
    /// they are told. An audit failure must not replace it — a 400 becoming a
    /// 500 would tell the caller their token was fine.
    /// </summary>
    [Fact]
    public async Task A_failed_autonomous_write_does_not_mask_the_commands_own_failure()
    {
        await RunAsync(async (_, fixture) =>
        {
            await using var broken = BuildProvider(autonomousWriterThrows: true);
            using var scope = broken.CreateScope();

            var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
                () => scope.ServiceProvider
                    .GetRequiredService<ICommandDispatcher>()
                    .SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                        new ActivateAccountCommand(fixture.TokenPlainText + "x", GoodPassword),
                        CancellationToken.None));

            Assert.Equal("The activation token is not valid.", failure.Message);

            // Lost to nobody: it travels with the exception that won, and the
            // host logs it from there.
            Assert.Contains(
                "The autonomous writer is broken.",
                Assert.IsType<string>(failure.Data["Ligature.AutonomousAuditFailure"]));
        });
    }

    /// <summary>
    /// Behaviour 14's secret scan is a safety net, not the design. The design
    /// is that neither the token the bearer presented nor the hash of the
    /// password they chose is ever put into a declaration.
    /// </summary>
    [Fact]
    public async Task Neither_the_token_nor_the_password_hash_reaches_the_trail()
    {
        await RunAsync(async (dispatcher, fixture) =>
        {
            await dispatcher.SendAsync<ActivateAccountCommand, ActivateAccountResult>(
                new ActivateAccountCommand(fixture.TokenPlainText, GoodPassword),
                CancellationToken.None);

            var trail = await DumpTrailAsync();

            Assert.DoesNotContain(fixture.TokenPlainText, trail, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Secret, trail, StringComparison.Ordinal);
            Assert.DoesNotContain(GoodPassword, trail, StringComparison.Ordinal);

            var credential = await ReadCredentialAsync(fixture.IdentityId);

            Assert.DoesNotContain(credential.PasswordHash, trail, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------- trail readers

    private sealed record TrailRecord(
        Guid AuditId, string EventType, string WritePath, string OriginKind,
        Guid? ActorUserId, string? ActorType, Guid? AuthorizingRoleId,
        Guid? AuthorizingAssignmentId, string EntityType, Guid? EntityId,
        Guid OperationId, Guid? CausationId, string? Before, string? After,
        string? Payload);

    /// <summary>
    /// The records of THIS activation, in sequence order.
    ///
    /// The database belongs to the class rather than to one test, so the trail
    /// also holds what the other tests did. The operation is located from the
    /// one record whose subject this test knows — the token it presented — and
    /// an activation that wrote nothing simply finds no operation, which is
    /// what the "records nothing" tests assert.
    /// </summary>
    private async Task<IReadOnlyList<TrailRecord>> ReadTrailAsync(Fixture fixture)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT audit_id, event_type, write_path, origin_kind, actor_user_id,
                   actor_type, authorizing_role_id, authorizing_assignment_id,
                   entity_type, entity_id, operation_id, causation_id,
                   before::text, after::text, payload::text
            FROM audit.audit_record
            WHERE operation_id = (
                SELECT operation_id FROM audit.audit_record
                WHERE event_type = 'TokenConsumed' AND entity_id = @tokenId)
            ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("tokenId", fixture.TokenId.Value);

        var records = new List<TrailRecord>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new TrailRecord(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return records;
    }

    /// <summary>
    /// Rejections stand alone: the command that caused them rolled back, so
    /// there is no other record of that operation to find them by.
    /// </summary>
    private async Task<IReadOnlyList<TrailRecord>> ReadRejectionsAsync(
        Guid? tokenId, long after = 0)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            SELECT audit_id, event_type, write_path, origin_kind, actor_user_id,
                   actor_type, authorizing_role_id, authorizing_assignment_id,
                   entity_type, entity_id, operation_id, causation_id,
                   before::text, after::text, payload::text
            FROM audit.audit_record
            WHERE event_type = 'TokenRejected'
              AND sequence > @after
              AND entity_id IS {(tokenId is null ? "NULL" : "NOT DISTINCT FROM @tokenId")}
            ORDER BY sequence
            """, connection);

        command.Parameters.AddWithValue("after", after);

        if (tokenId is not null)
            command.Parameters.AddWithValue("tokenId", tokenId.Value);

        var records = new List<TrailRecord>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            records.Add(new TrailRecord(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.GetGuid(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return records;
    }

    private async Task<long> ReadLastSequenceAsync()
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT coalesce(max(sequence), 0) FROM audit.audit_record", connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<IReadOnlyList<string>> ReadReferencesAsync(Guid auditId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT entity_type || '/' || entity_id || '/' || ref_role
            FROM audit.audit_entity_ref
            WHERE audit_id = @id
            ORDER BY entity_type, ref_role
            """, connection);

        command.Parameters.AddWithValue("id", auditId);

        var refs = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            refs.Add(reader.GetString(0));

        return refs;
    }

    /// <summary>Every record and every ref as text, for the absence checks.</summary>
    private async Task<string> DumpTrailAsync()
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg(to_jsonb(r)::text, ' '), '')
                   || ' '
                   || coalesce((SELECT string_agg(to_jsonb(e)::text, ' ')
                                FROM audit.audit_entity_ref e), '')
            FROM audit.audit_record r
            """, connection);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    // ------------------------------------------------------------- harness

    private sealed record Fixture(
        UserId UserId,
        UserIdentityId IdentityId,
        UserTokenId TokenId,
        string Secret,
        string TokenPlainText);

    private async Task RunAsync(
        Func<ICommandDispatcher, Fixture, Task> body)
    {
        var fixture = await SeedAsync();

        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Deliberately NO execution context is established here: CRD-C1 is an
        // anonymous command, the pipeline must let it through, and the caller
        // it ends up acting as is the one its own token consumption
        // established.
        //
        // Nothing is deleted afterwards. The user this seeded is an audit
        // actor by the time the command returns, and the database is thrown
        // away with the class instead.
        await body(
            scope.ServiceProvider.GetRequiredService<ICommandDispatcher>(),
            fixture);
    }

    private ServiceProvider BuildProvider()
        => BuildProvider(autonomousWriterThrows: false);

    private ServiceProvider BuildProvider(bool autonomousWriterThrows)
    {
        var services = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString);

        if (autonomousWriterThrows)
            services.AddSingleton<IAutonomousAuditRecordWriter>(new ThrowingAutonomousWriter());

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class ThrowingAutonomousWriter : IAutonomousAuditRecordWriter
    {
        public Task WriteAsync(
            IReadOnlyList<AuditRecordRow> rows, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The autonomous writer is broken.");
    }

    /// <summary>
    /// A user with a local identity and no credential — exactly the state
    /// USR-C1 leaves behind (inv. 15).
    /// </summary>
    private async Task<Fixture> SeedAsync()
    {
        var discriminator = Guid.NewGuid().ToString("N");

        var user = User.CreateHuman(
            UserId.New(),
            "Pending",
            "Activation",
            $"Pending Activation {discriminator[..8]}",
            $"activate-{discriminator}@example.test",
            Now,
            User.SystemUserId);

        var identity = UserIdentity.CreateLocal(
            UserIdentityId.New(),
            user.Id,
            ActorType.Human,
            $"activate-{discriminator[..12]}",
            Now,
            User.SystemUserId);

        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        var token = UserToken.Create(
            tokenId,
            identity.Id,
            TokenType.Activation,
            material.Hash,
            Now,
            Now + SecurityBaseline.Current.ActivationTokenLifetime,
            User.SystemUserId);

        await using var context = CreateContext();
        context.AddRange(user, identity, token);
        await context.SaveChangesAsync(CancellationToken.None);

        var separator = material.PlainText.IndexOf('.');

        return new Fixture(
            user.Id,
            identity.Id,
            tokenId,
            material.PlainText[(separator + 1)..],
            material.PlainText);
    }

    /// <summary>
    /// Issues an extra token in a chosen terminal state, returning its
    /// delivered form.
    /// </summary>
    private async Task DeactivateAsync(string table, Guid id)
        => await SetStatusAsync(table, id, "Inactive");

    private async Task ReactivateAsync(string table, Guid id)
        => await SetStatusAsync(table, id, "Active");

    /// <summary>
    /// Status only. The deactivation stamp columns are a both-or-neither pair
    /// independent of it, and nothing here reads them.
    /// </summary>
    private async Task SetStatusAsync(string table, Guid id, string status)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"UPDATE {table} SET status = @status WHERE id = @id", connection);

        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", id);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A bare extra local identity, for tests needing an open token of a type
    /// the fixture's own identity has already used up (UT4).
    /// </summary>
    private async Task<UserIdentityId> SeedIdentityAsync(UserId userId)
    {
        var id = Guid.NewGuid();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_identity
                (id, user_id, actor_type, identity_type, identity_provider,
                 subject_id, username, status, created_at, created_by)
            VALUES
                (@id, @userId, 'Human', 'Local', 'Application',
                 @id, @username, 'Active', now(), @system)
            """, connection);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId.Value);
        command.Parameters.AddWithValue("username", $"extra-{id:N}");
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();

        return new UserIdentityId(id);
    }

    private async Task<(UserTokenId Id, string PlainText)> IssueTokenAsync(
        UserIdentityId identityId,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? invalidatedAt = null,
        DateTimeOffset? createdAt = null,
        TokenType tokenType = TokenType.Activation)
    {
        var tokenId = UserTokenId.New();
        var material = new UserTokenService().Generate(tokenId);

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_token
                (id, user_identity_id, token_type, token_hash, expires_at,
                 used_at, invalidated_at, created_at, created_by)
            VALUES
                (@id, @identityId, @tokenType, @hash, @expiresAt,
                 NULL, @invalidatedAt, @now, @system)
            """, connection);

        command.Parameters.AddWithValue("tokenType", tokenType.ToString());

        command.Parameters.AddWithValue("id", tokenId.Value);
        command.Parameters.AddWithValue("identityId", identityId.Value);
        command.Parameters.AddWithValue("hash", material.Hash);
        command.Parameters.AddWithValue("expiresAt", expiresAt ?? Now.AddDays(1));
        command.Parameters.AddWithValue(
            "invalidatedAt", (object?)invalidatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("now", createdAt ?? Now);
        command.Parameters.AddWithValue("system", User.SystemUserId.Value);

        await command.ExecuteNonQueryAsync();

        return (tokenId, material.PlainText);
    }

    private LigatureDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<LigatureDbContext>()
            .UseNpgsql(_database.ConnectionString)
            .AddInterceptors(
                new ProvenanceStampingInterceptor(
                    new FixedClock(Now), executionContext: null))
            .Options;

        return new LigatureDbContext(options);
    }

    // ------------------------------------------------------------- readers

    private sealed record CredentialRow(
        string IdentityType, string PasswordHash, string PasswordAlgorithm,
        bool MustChangePassword, int FailedAttemptCount,
        DateTimeOffset? LockedUntil, Guid CreatedBy);

    private async Task<CredentialRow> ReadCredentialAsync(
        UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT identity_type, password_hash, password_algorithm,
                   must_change_password, failed_attempt_count, locked_until,
                   created_by
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No credential row was written.");

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetBoolean(3), reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetGuid(6));
    }

    private sealed record HistoryRow(string PasswordHash, string PasswordAlgorithm);

    private async Task<HistoryRow> ReadHistoryAsync(UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm
            FROM password_history WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "No history row was written.");

        return new HistoryRow(reader.GetString(0), reader.GetString(1));
    }

    private async Task<DateTimeOffset?> ReadTokenUsedAtAsync(UserTokenId id)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT used_at FROM user_token WHERE id = @id", connection);

        command.Parameters.AddWithValue("id", id.Value);

        var value = await command.ExecuteScalarAsync();

        // Npgsql surfaces timestamptz as DateTime through the untyped scalar.
        return value switch
        {
            null or DBNull => null,
            DateTimeOffset offset => offset,
            DateTime utc => new DateTimeOffset(
                DateTime.SpecifyKind(utc, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("Not a timestamp."),
        };
    }

    private async Task<string> DumpRowAsync(
        string table, UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT {table}::text FROM {table} WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private Task<int> CountCredentialsAsync(UserIdentityId identityId)
        => CountAsync("credential", identityId);

    private Task<int> CountHistoryAsync(UserIdentityId identityId)
        => CountAsync("password_history", identityId);

    private async Task<int> CountAsync(
        string table, UserIdentityId identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {table} WHERE user_identity_id = @id",
            connection);

        command.Parameters.AddWithValue("id", identityId.Value);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }


    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }
}
