using Ligature.Platform.Application;
using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Execution;
using Ligature.Platform.Application.Users.Commands.ChangePassword;
using Ligature.Platform.Domain.Users;
using Ligature.Platform.Persistence.Services;
using Ligature.SharedKernel.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// CRD-C4 end to end: an established caller, the session they present, the
/// real hasher, dispatch through the pipeline, and the rows read back.
///
/// Session timestamps are computed from the same clock the handler reads, not
/// from PostgreSQL's now(), so the idle-window boundary — the one place
/// "active" is decided by seconds — is exact rather than hostage to clock drift
/// between the application and the database.
///
/// EVERY REFUSAL is compared against a footprint of everything the command
/// writes. What that proves is that nothing COMMITS; the checks' ordering is a
/// property of the handler's code, which the refusal's rollback hides.
///
/// Its own provisioned database: a user who changes a password becomes the
/// actor of audit records and can never be removed.
/// </summary>
public sealed class ChangePasswordIntegrationTests
    : IClassFixture<ActivationDatabase>
{
    private const string Current = "the-current-password-1";
    private const string Fresh = "an-entirely-new-password-2";

    private static readonly TimeSpan Idle = SecurityBaseline.Current.SessionIdleTimeout;
    private static readonly TimeSpan Tolerance = CallerEstablisher.EnforcementTolerance;

    private readonly ActivationDatabase _database;

    public ChangePasswordIntegrationTests(ActivationDatabase database)
        => _database = database;

    // ------------------------------------------------------------------
    // It works
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_password_changes_the_flag_clears_and_history_grows()
    {
        var subject = await SeedAsync(mustChangePassword: true);
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var historyBefore = await CountHistoryAsync(subject.IdentityId);
        var before = await ReadCredentialAsync(subject.IdentityId);

        await DispatchAsync(subject.UserId, current, Current, Fresh);

        var after = await ReadCredentialAsync(subject.IdentityId);
        var hasher = new PasswordHasher();

        Assert.True(hasher.Verify(Fresh, after.Hash, after.Algorithm).IsValid);
        Assert.False(hasher.Verify(Current, after.Hash, after.Algorithm).IsValid);

        Assert.False(after.MustChangePassword);
        Assert.NotEqual(before.ChangedAt, after.ChangedAt);
        Assert.Equal(historyBefore + 1, await CountHistoryAsync(subject.IdentityId));

        var record = Assert.Single(await ReadChangeRecordsAsync(subject.IdentityId));

        Assert.Equal("Authenticated", record.OriginKind);
        Assert.Equal(subject.UserId.Value, record.ActorUserId);
        Assert.Equal("Credential", record.EntityType);
        Assert.Equal(after.Algorithm, record.Algorithm);
        Assert.DoesNotContain(after.Hash, record.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(Fresh, record.Payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A5 and D1a, at every boundary. Revoked: another active session of this
    /// identity, and one idle past the timeout but still inside the enforcement
    /// tolerance — the per-request check would still accept it. Untouched: the
    /// session making the request; one idle past timeout AND tolerance; one
    /// already revoked; one long past absolute expiry; one that expired seconds
    /// ago despite recent activity, so expiry alone is what excludes it; and an
    /// active session of the same user's OTHER identity.
    /// </summary>
    [Fact]
    public async Task Only_this_identitys_other_live_sessions_are_revoked()
    {
        var subject = await SeedAsync();
        var otherIdentity = await AddExternalIdentityAsync(subject.UserId);

        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var other = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var withinTolerance = await InsertSessionAsync(subject.IdentityId, SessionKind.IdleWithinTolerance);
        var pastTolerance = await InsertSessionAsync(subject.IdentityId, SessionKind.IdlePastTolerance);
        var alreadyRevoked = await InsertSessionAsync(subject.IdentityId, SessionKind.Revoked);
        var expired = await InsertSessionAsync(subject.IdentityId, SessionKind.Expired);
        var expiredButRecent = await InsertSessionAsync(subject.IdentityId, SessionKind.ExpiredRecentlyActive);
        var otherIdentitysSession = await InsertSessionAsync(otherIdentity, SessionKind.Active);

        await DispatchAsync(subject.UserId, current, Current, Fresh);

        foreach (var revoked in new[] { other, withinTolerance })
        {
            var row = await ReadSessionAsync(revoked);

            Assert.True(row.IsRevoked);
            Assert.Equal(subject.UserId.Value, row.RevokedBy);
            Assert.Equal("PasswordChanged", row.Reason);
        }

        Assert.False((await ReadSessionAsync(current)).IsRevoked);
        Assert.False((await ReadSessionAsync(pastTolerance)).IsRevoked);
        Assert.False((await ReadSessionAsync(expired)).IsRevoked);
        Assert.False((await ReadSessionAsync(expiredButRecent)).IsRevoked);
        Assert.False((await ReadSessionAsync(otherIdentitysSession)).IsRevoked);

        // Already revoked: its first revocation is write-once and survives.
        var earlier = await ReadSessionAsync(alreadyRevoked);
        Assert.Equal("Logout", earlier.Reason);
        Assert.Equal(User.SystemUserId.Value, earlier.RevokedBy);

        var change = Assert.Single(await ReadChangeRecordsAsync(subject.IdentityId));

        Assert.Equal("true", change.OtherSessionsRevoked);

        foreach (var revoked in new[] { other, withinTolerance })
        {
            var record = Assert.Single(await ReadRevocationRecordsAsync(revoked));

            Assert.Equal("Authenticated", record.OriginKind);
            Assert.Equal(subject.UserId.Value, record.ActorUserId);
            Assert.Equal("PasswordChanged", record.Reason);
            Assert.Equal(change.AuditId, record.CausationId);
        }

        foreach (var untouched in new[] { current, pastTolerance, alreadyRevoked, expired, expiredButRecent, otherIdentitysSession })
            Assert.Empty(await ReadRevocationRecordsAsync(untouched));
    }

    [Fact]
    public async Task With_no_other_session_nothing_is_revoked_and_the_record_says_so()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await DispatchAsync(subject.UserId, current, Current, Fresh);

        var change = Assert.Single(await ReadChangeRecordsAsync(subject.IdentityId));

        Assert.Equal("false", change.OtherSessionsRevoked);
        Assert.False((await ReadSessionAsync(current)).IsRevoked);
        Assert.Equal(0, await CountRevocationsForIdentityAsync(subject.IdentityId));
    }

    // ------------------------------------------------------------------
    // The current password, and the lock
    // ------------------------------------------------------------------

    /// <summary>
    /// D2 — refused, and the CREDENTIAL never moves: not its counter, not its
    /// lock. Below the session threshold nothing else commits either, except
    /// the session's own counter (see "The attempt limit" below).
    /// </summary>
    [Fact]
    public async Task A_wrong_current_password_is_refused_and_never_counts_toward_lockout()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        var attempts = SecurityBaseline.Current.MaxFailedLoginAttempts - 1;
        var before = await FootprintAsync(subject);

        for (var i = 0; i < attempts; i++)
        {
            var result = await DispatchAsync(subject.UserId, current, "not-the-password-9", Fresh);

            Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
        }

        Assert.Equal(before, await FootprintAsync(subject));

        var credential = await ReadCredentialAsync(subject.IdentityId);

        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.False(credential.IsLocked);
        Assert.Equal(0, await CountRecordsAsync("AccountLocked", subject.IdentityId));
    }

    /// <summary>
    /// D3 — a live lock refuses even the RIGHT current password, with the same
    /// message a wrong one gets. A valid session is not a way around lockout.
    /// </summary>
    [Fact]
    public async Task A_locked_credential_is_refused_with_the_generic_error()
    {
        var subject = await SeedAsync(lockedOut: true);
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        var locked = await DispatchAsync(subject.UserId, current, Current, Fresh);

        // The endpoint answers Refused with the one generic message (L8: a
        // counted refusal, alike in every way to a wrong password).
        Assert.Equal(ChangePasswordOutcome.Refused, locked.Outcome);
        Assert.Equal(await GenericMessageAsync(), ChangePasswordResult.NotChanged);
        Assert.Equal(before, await FootprintAsync(subject));
        Assert.True((await ReadCredentialAsync(subject.IdentityId)).IsLocked);
    }

    /// <summary>
    /// The current password is judged FIRST. With a wrong current password, a
    /// new password that is too short or reused changes nothing about the
    /// answer — otherwise the new-password checks would be an oracle for the
    /// current one.
    /// </summary>
    [Theory]
    [InlineData("short")]
    [InlineData(Current)]
    public async Task A_wrong_current_password_hides_whether_the_new_one_was_acceptable(string newPassword)
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        var failure = await DispatchAsync(subject.UserId, current, "not-the-password-9", newPassword);

        Assert.Equal(ChangePasswordOutcome.Refused, failure.Outcome);
    }

    // ------------------------------------------------------------------
    // The new password
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_new_password_below_the_floor_is_refused()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, "short"));

        // A policy error, not the generic one: it reveals nothing about the
        // current password, which has already verified.
        Assert.NotEqual(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(subject));
    }

    [Fact]
    public async Task The_current_password_cannot_be_chosen_again()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, Current));

        Assert.NotEqual(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(subject));
    }

    [Fact]
    public async Task An_older_password_inside_the_history_depth_cannot_be_chosen_again()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        const string Older = "an-older-password-from-before-3";

        await InsertHistoryAsync(subject.IdentityId, new PasswordHasher().Hash(Older).Hash, minutesAgo: 60 * 24 * 7);

        var before = await FootprintAsync(subject);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, Older));

        Assert.Equal(before, await FootprintAsync(subject));
    }

    // ------------------------------------------------------------------
    // Sessions this command cannot act for
    // ------------------------------------------------------------------

    /// <summary>
    /// The ownership check. The stranger knows the victim's current password
    /// here, so only the check stands between them and the change.
    /// </summary>
    [Fact]
    public async Task Another_users_session_is_refused()
    {
        var victim = await SeedAsync();
        var stranger = await SeedAsync();
        var victimsSession = await InsertSessionAsync(victim.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(victim);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(stranger.UserId, victimsSession, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(victim));
    }

    [Fact]
    public async Task An_unknown_session_is_refused()
    {
        var subject = await SeedAsync();
        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, UserSessionId.New().Value, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(subject));
    }

    /// <summary>
    /// D4 — the session names the identity. A session held through an external
    /// identity is refused even though the same user has a local credential,
    /// and that credential is untouched.
    /// </summary>
    [Fact]
    public async Task A_session_of_an_external_identity_is_refused()
    {
        var subject = await SeedAsync();
        var external = await AddExternalIdentityAsync(subject.UserId);
        var externalSession = await InsertSessionAsync(external, SessionKind.Active);
        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, externalSession, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(subject));
    }

    /// <summary>
    /// The sharper form of the case above. The schema does NOT stop an external
    /// identity holding a credential row: the composite foreign key only
    /// requires the row's identity_type to match its identity's, and no CHECK
    /// pins it to 'Local'. So a credential lookup alone would not refuse this
    /// session — the Local-identity check is what does, and the password it
    /// holds is never changed through a session it did not authenticate.
    /// </summary>
    [Fact]
    public async Task A_session_of_an_external_identity_is_refused_even_if_it_holds_a_credential()
    {
        var subject = await SeedAsync();
        var external = await AddExternalIdentityAsync(subject.UserId);
        var externalSession = await InsertSessionAsync(external, SessionKind.Active);

        var stray = new PasswordHasher().Hash(Current);

        await ExecuteAsync(
            $"""
             INSERT INTO credential
                 (id, user_identity_id, identity_type, password_hash,
                  password_algorithm, password_changed_at, must_change_password,
                  failed_attempt_count, locked_until, created_at, created_by)
             VALUES
                 ('{Guid.NewGuid()}', '{external}', 'External', '{stray.Hash}',
                  '{stray.Algorithm}', now() - interval '1 day', false, 0, NULL,
                  now() - interval '1 day', '{User.SystemUserId.Value}')
             """);

        var externalSubject = new Subject(subject.UserId, external);
        var before = await FootprintAsync(externalSubject);
        var localBefore = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, externalSession, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(externalSubject));
        Assert.Equal(localBefore, await FootprintAsync(subject));
    }

    /// <summary>
    /// The per-request check refuses a session whose identity is inactive, but
    /// the command is reachable from any composition edge and does not trust
    /// that it ran.
    /// </summary>
    [Fact]
    public async Task A_session_of_an_inactive_identity_is_refused()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await ExecuteAsync(
            $"""
             UPDATE user_identity
             SET status = 'Inactive', deactivated_at = now(), deactivated_by = '{User.SystemUserId.Value}'
             WHERE id = '{subject.IdentityId}'
             """);

        var before = await FootprintAsync(subject);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(before, await FootprintAsync(subject));
    }

    [Fact]
    public async Task An_identity_without_a_credential_is_refused()
    {
        var subject = await SeedAsync(withCredential: false);
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, Fresh));

        Assert.Equal(await GenericMessageAsync(), failure.Message);
        Assert.Equal(0, await CountHistoryAsync(subject.IdentityId));
    }

    // ------------------------------------------------------------------
    // Callers
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => DispatchAsync(caller: null, current, Current, Fresh));

        Assert.Equal(before, await FootprintAsync(subject));
    }

    [Fact]
    public async Task A_non_human_caller_is_refused_by_the_pipeline()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, Fresh, ActorType.Agent));

        Assert.Equal(before, await FootprintAsync(subject));
    }

    // ------------------------------------------------------------------

    private enum SessionKind
    {
        Active,
        IdleWithinTolerance,
        IdlePastTolerance,
        Revoked,
        Expired,
        ExpiredRecentlyActive,
    }

    // ------------------------------------------------------------------
    // The attempt limit (docs/requirements.md, "CRD-C4 — limiting
    // current-password attempts per session", L1–L8)
    // ------------------------------------------------------------------

    private const string Wrong = "not-the-password-9";

    private const string LimitExplanation = "Too many incorrect current passwords while changing the password";

    private static readonly int N = SecurityBaseline.Current.MaxFailedLoginAttempts;

    /// <summary>
    /// AC-1 — below N each wrong attempt is the uniform refusal, and the count
    /// COMMITS although the command refused: read back from a fresh
    /// connection every time. Nothing else moves and nothing is audited.
    /// </summary>
    [Fact]
    public async Task Below_the_threshold_each_wrong_attempt_is_refused_and_its_count_commits()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var before = await FootprintAsync(subject);

        for (var attempt = 1; attempt < N; attempt++)
        {
            var result = await DispatchAsync(subject.UserId, current, Wrong, Fresh);

            Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
            Assert.Equal(attempt, await ReadAttemptsAsync(current));
        }

        Assert.Equal(before, await FootprintAsync(subject));
        Assert.False((await ReadSessionAsync(current)).IsRevoked);
    }

    /// <summary>
    /// AC-2 — the Nth wrong attempt ends THIS session: revoked by the account
    /// holder with the controlled code, recorded once as SessionRevoked with
    /// the explanation, and the code in After (R1). Once ended, a further
    /// attempt on it changes nothing and counts nothing.
    /// </summary>
    [Fact]
    public async Task The_Nth_wrong_attempt_ends_this_session_and_records_it_once()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        for (var attempt = 1; attempt < N; attempt++)
            await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        var last = await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        Assert.Equal(ChangePasswordOutcome.SessionEnded, last.Outcome);

        var session = await ReadSessionAsync(current);
        Assert.True(session.IsRevoked);
        Assert.Equal(subject.UserId.Value, session.RevokedBy);
        Assert.Equal("PasswordChangeAttemptsExceeded", session.Reason);
        Assert.Equal(N, await ReadAttemptsAsync(current));

        var record = Assert.Single(await ReadRevocationRecordsAsync(current));
        Assert.Equal("Authenticated", record.OriginKind);
        Assert.Equal(subject.UserId.Value, record.ActorUserId);
        Assert.Equal(LimitExplanation, record.Reason);
        Assert.Equal("PasswordChangeAttemptsExceeded", await ReadAfterReasonAsync(current));

        var again = await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        Assert.Equal(ChangePasswordOutcome.SessionEnded, again.Outcome);
        Assert.Equal(N, await ReadAttemptsAsync(current));
        Assert.Single(await ReadRevocationRecordsAsync(current));
    }

    /// <summary>
    /// AC-3 — the blast radius is this session. The account's other session
    /// stays active, the credential is neither counted nor locked, and CRD-C4
    /// never produces AccountLocked (SES-C1 remains its only producer).
    /// </summary>
    [Fact]
    public async Task Ending_the_session_touches_nothing_else()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);
        var other = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        for (var attempt = 1; attempt <= N; attempt++)
            await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        Assert.True((await ReadSessionAsync(current)).IsRevoked);
        Assert.False((await ReadSessionAsync(other)).IsRevoked);
        Assert.Equal(0, await ReadAttemptsAsync(other));

        var credential = await ReadCredentialAsync(subject.IdentityId);
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.False(credential.IsLocked);
        Assert.Equal(0, await CountRecordsAsync("AccountLocked", subject.IdentityId));
    }

    /// <summary>
    /// AC-5 (L3) — a successful change resets the count, so N−1 more wrong
    /// attempts after it do not end the session.
    /// </summary>
    [Fact]
    public async Task A_successful_change_resets_the_count()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        for (var attempt = 1; attempt < N; attempt++)
            await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        var changed = await DispatchAsync(subject.UserId, current, Current, Fresh);

        Assert.Equal(ChangePasswordOutcome.Changed, changed.Outcome);
        Assert.Equal(0, await ReadAttemptsAsync(current));

        for (var attempt = 1; attempt < N; attempt++)
        {
            var result = await DispatchAsync(subject.UserId, current, Wrong, "yet-another-password-3");

            Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
        }

        Assert.False((await ReadSessionAsync(current)).IsRevoked);
        Assert.Equal(N - 1, await ReadAttemptsAsync(current));
    }

    /// <summary>
    /// AC-6 — the current password was RIGHT; only the new one was refused.
    /// That is neither a failed attempt nor a success: the count stays.
    /// </summary>
    [Theory]
    [InlineData("short")]
    [InlineData(Current)]
    public async Task A_refused_new_password_neither_counts_nor_resets(string newPassword)
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await DispatchAsync(subject.UserId, current, Wrong, Fresh);
        await DispatchAsync(subject.UserId, current, Wrong, Fresh);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, Current, newPassword));

        Assert.Equal(2, await ReadAttemptsAsync(current));
    }

    /// <summary>
    /// AC-7 (L8) — a refusal because the credential is locked counts exactly
    /// as a wrong password does, and the Nth ends the session.
    /// </summary>
    [Fact]
    public async Task Refusals_of_a_locked_credential_count_and_the_Nth_ends_the_session()
    {
        var subject = await SeedAsync(lockedOut: true);
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        for (var attempt = 1; attempt < N; attempt++)
        {
            var result = await DispatchAsync(subject.UserId, current, Current, Fresh);

            Assert.Equal(ChangePasswordOutcome.Refused, result.Outcome);
            Assert.Equal(attempt, await ReadAttemptsAsync(current));
        }

        var last = await DispatchAsync(subject.UserId, current, Current, Fresh);

        Assert.Equal(ChangePasswordOutcome.SessionEnded, last.Outcome);
        Assert.Equal("PasswordChangeAttemptsExceeded", (await ReadSessionAsync(current)).Reason);
    }

    // ---- AC-8: concurrency, proved with real transactions

    /// <summary>
    /// The lock is HELD for the whole decision. Another transaction holds the
    /// user's row lock and moves the count to N−1; the attempt must wait, then
    /// decide on what committed — crossing N. An attempt that read the session
    /// before (or without) the lock would have counted from 0 and refused.
    /// </summary>
    [Fact]
    public async Task An_attempt_waits_for_the_users_lock_and_decides_on_what_committed_meanwhile()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await HolderExecuteAsync(holder, transaction, $"SELECT 1 FROM app_user WHERE id = '{subject.UserId.Value}' FOR UPDATE");

        var attempt = Task.Run(() => DispatchAsync(subject.UserId, current, Wrong, Fresh));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(attempt.IsCompleted, "the attempt did not wait for the user's row lock");

        await HolderExecuteAsync(holder, transaction,
            $"UPDATE user_session SET failed_password_change_attempts = {N - 1} WHERE id = '{current}'");

        await transaction.CommitAsync();

        Assert.Equal(ChangePasswordOutcome.SessionEnded, (await attempt).Outcome);
        Assert.Equal(N, await ReadAttemptsAsync(current));
        Assert.Single(await ReadRevocationRecordsAsync(current));
    }

    /// <summary>
    /// The clock is read AFTER the lock (docs/architecture.md, "Read the clock
    /// after the lock"). While the attempt waits at N−1, activity is recorded
    /// on the session at the real moment of the write — clock_timestamp(), as
    /// the deactivation test does. The revocation that follows must not be
    /// dated before it: a clock read before the wait would record the session
    /// as ended earlier than activity it went on to have.
    /// </summary>
    [Fact]
    public async Task The_revocation_is_dated_after_everything_that_committed_while_the_attempt_waited()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await ExecuteAsync($"UPDATE user_session SET failed_password_change_attempts = {N - 1} WHERE id = '{current}'");

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await HolderExecuteAsync(holder, transaction, $"SELECT 1 FROM app_user WHERE id = '{subject.UserId.Value}' FOR UPDATE");

        var attempt = Task.Run(() => DispatchAsync(subject.UserId, current, Wrong, Fresh));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(attempt.IsCompleted, "the attempt did not wait for the user's row lock");

        await HolderExecuteAsync(holder, transaction,
            $"UPDATE user_session SET last_activity_at = clock_timestamp() WHERE id = '{current}'");

        await transaction.CommitAsync();

        Assert.Equal(ChangePasswordOutcome.SessionEnded, (await attempt).Outcome);
        Assert.True(
            await ScalarAsync<bool>($"SELECT revoked_at >= last_activity_at FROM user_session WHERE id = '{current}'"),
            "the revocation is dated before activity that committed while the attempt waited");
    }

    /// <summary>
    /// The session is re-checked UNDER the lock. It is revoked while the
    /// attempt waits; the attempt must then answer SessionEnded and count
    /// nothing, rather than act on the session it saw before waiting.
    /// </summary>
    [Fact]
    public async Task A_session_ended_while_the_attempt_waited_is_not_counted()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await HolderExecuteAsync(holder, transaction, $"SELECT 1 FROM app_user WHERE id = '{subject.UserId.Value}' FOR UPDATE");

        var attempt = Task.Run(() => DispatchAsync(subject.UserId, current, Wrong, Fresh));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(attempt.IsCompleted, "the attempt did not wait for the user's row lock");

        await HolderExecuteAsync(holder, transaction,
            $"""
             UPDATE user_session
                SET revoked_at = now(), revoked_by = '{subject.UserId.Value}', revocation_reason = 'Logout'
              WHERE id = '{current}'
             """);

        await transaction.CommitAsync();

        Assert.Equal(ChangePasswordOutcome.SessionEnded, (await attempt).Outcome);
        Assert.Equal(0, await ReadAttemptsAsync(current));
        Assert.Empty(await ReadRevocationRecordsAsync(current));
    }

    /// <summary>
    /// Two attempts on ONE session, released together by a lock the test
    /// holds so that both are genuinely in flight. At N−1 exactly one crosses
    /// N: one revocation, one record, and both answer SessionEnded. Without
    /// the serialisation both would read N−1, and both would revoke.
    /// </summary>
    [Theory]
    [InlineData(1, new[] { ChangePasswordOutcome.SessionEnded, ChangePasswordOutcome.SessionEnded })]
    [InlineData(2, new[] { ChangePasswordOutcome.Refused, ChangePasswordOutcome.SessionEnded })]
    public async Task Concurrent_attempts_on_one_session_cross_the_threshold_exactly_once(
        int belowThreshold, ChangePasswordOutcome[] expected)
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        await ExecuteAsync($"UPDATE user_session SET failed_password_change_attempts = {N - belowThreshold} WHERE id = '{current}'");

        await using var holder = await _database.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();

        await HolderExecuteAsync(holder, transaction, $"SELECT 1 FROM app_user WHERE id = '{subject.UserId.Value}' FOR UPDATE");

        var first = Task.Run(() => DispatchAsync(subject.UserId, current, Wrong, Fresh));
        var second = Task.Run(() => DispatchAsync(subject.UserId, current, Wrong, Fresh));

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        Assert.False(first.IsCompleted || second.IsCompleted, "an attempt did not wait for the user's row lock");

        await transaction.CommitAsync();

        var outcomes = (await Task.WhenAll(first, second)).Select(x => x.Outcome).Order().ToArray();

        Assert.Equal(expected.Order().ToArray(), outcomes);
        Assert.Equal(N, await ReadAttemptsAsync(current));
        Assert.Single(await ReadRevocationRecordsAsync(current));
        Assert.Equal("PasswordChangeAttemptsExceeded", (await ReadSessionAsync(current)).Reason);
    }

    /// <summary>AC-9 — the database refuses a negative count (L6).</summary>
    [Fact]
    public async Task The_database_refuses_a_negative_count()
    {
        var subject = await SeedAsync();
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        var refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync($"UPDATE user_session SET failed_password_change_attempts = -1 WHERE id = '{current}'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
    }

    private Task<int> ReadAttemptsAsync(Guid sessionId)
        => ScalarAsync<int>($"SELECT failed_password_change_attempts FROM user_session WHERE id = '{sessionId}'");

    private async Task<string?> ReadAfterReasonAsync(Guid sessionId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT after::jsonb ->> 'RevocationReason'
            FROM audit.audit_record
            WHERE event_type = 'SessionRevoked' AND entity_type = 'Session' AND entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);

        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task HolderExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);

        await command.ExecuteNonQueryAsync();
    }

    private sealed record Subject(UserId UserId, Guid IdentityId);

    private sealed record CredentialRow(
        string Hash, string Algorithm, string ChangedAt, bool MustChangePassword,
        int FailedAttemptCount, bool IsLocked);

    private sealed record SessionRow(bool IsRevoked, Guid? RevokedBy, string? Reason);

    private sealed record ChangeRecord(
        Guid AuditId, string OriginKind, Guid? ActorUserId, string EntityType,
        string Payload, string? Algorithm, string? OtherSessionsRevoked);

    private sealed record RevocationRecord(
        string OriginKind, Guid? ActorUserId, string? Reason, Guid? CausationId);

    /// <summary>Everything CRD-C4 could write, for one subject.</summary>
    private sealed record Footprint(
        string Hash, bool MustChangePassword, int FailedAttemptCount, bool IsLocked,
        long History, long RevokedSessions, long AuditRefs);

    private async Task<Footprint> FootprintAsync(Subject subject)
    {
        var credential = await TryReadCredentialAsync(subject.IdentityId);

        return new Footprint(
            credential?.Hash ?? "",
            credential?.MustChangePassword ?? false,
            credential?.FailedAttemptCount ?? 0,
            credential?.IsLocked ?? false,
            await CountHistoryAsync(subject.IdentityId),
            await ScalarAsync<long>(
                $"SELECT count(*) FROM user_session WHERE user_identity_id = '{subject.IdentityId}' AND revoked_at IS NOT NULL"),
            await ScalarAsync<long>(
                $"""
                 SELECT count(*) FROM audit.audit_entity_ref
                 WHERE entity_id IN ('{subject.IdentityId}', '{subject.UserId.Value}')
                 """));
    }

    /// <summary>
    /// The generic refusal, captured rather than hard-coded — from a refusal
    /// that still THROWS (no credential), since a wrong current password is
    /// now a counted refusal that returns.
    /// </summary>
    private async Task<string> GenericMessageAsync()
    {
        var subject = await SeedAsync(withCredential: false);
        var current = await InsertSessionAsync(subject.IdentityId, SessionKind.Active);

        var failure = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => DispatchAsync(subject.UserId, current, "not-the-password-9", Fresh));

        return failure.Message;
    }

    private Task<ChangePasswordResult> DispatchAsync(
        UserId? caller, Guid sessionId, string currentPassword, string newPassword,
        ActorType actorType = ActorType.Human)
        => DispatchAsync(caller, new UserSessionId(sessionId), currentPassword, newPassword, actorType);

    private async Task<ChangePasswordResult> DispatchAsync(
        UserId? caller, UserSessionId sessionId, string currentPassword, string newPassword,
        ActorType actorType = ActorType.Human)
    {
        await using var provider = new ServiceCollection()
            .AddPlatformApplication()
            .AddPlatformPersistence(_database.ConnectionString)
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();

        if (caller is not null)
        {
            scope.ServiceProvider
                .GetRequiredService<IExecutionContextInitializer>()
                .Establish(
                    caller,
                    actorType,
                    actorType == ActorType.Human
                        ? TestActorIdentity.Human()
                        : TestActorIdentity.NonHuman());
        }

        return await scope.ServiceProvider
            .GetRequiredService<ICommandDispatcher>()
            .SendAsync<ChangePasswordCommand, ChangePasswordResult>(
                new ChangePasswordCommand(sessionId, currentPassword, newPassword),
                CancellationToken.None);
    }

    private async Task<Subject> SeedAsync(
        bool withCredential = true, bool lockedOut = false, bool mustChangePassword = false)
    {
        var unique = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var system = User.SystemUserId.Value;

        await ExecuteAsync(
            $"""
             INSERT INTO app_user
                 (id, actor_type, first_name, last_name, display_name, email, status,
                  created_at, created_by, updated_at, updated_by)
             VALUES
                 ('{userId}', 'Human', 'Change', 'Password', 'Change Password',
                  'change-{unique}@example.test', 'Active',
                  now() - interval '1 day', '{system}', now(), '{system}');

             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{identityId}', '{userId}', 'Human', 'Local', 'Application',
                  '{identityId}', 'change-{unique}', 'Active',
                  now() - interval '1 day', '{system}');
             """);

        if (withCredential)
        {
            var current = new PasswordHasher().Hash(Current);

            await ExecuteAsync(
                $"""
                 INSERT INTO credential
                     (id, user_identity_id, identity_type, password_hash,
                      password_algorithm, password_changed_at, must_change_password,
                      failed_attempt_count, locked_until, created_at, created_by)
                 VALUES
                     ('{Guid.NewGuid()}', '{identityId}', 'Local', '{current.Hash}',
                      '{current.Algorithm}', now() - interval '1 day',
                      {(mustChangePassword ? "true" : "false")},
                      {(lockedOut ? SecurityBaseline.Current.MaxFailedLoginAttempts : 0)},
                      {(lockedOut ? "now() + interval '1 hour'" : "NULL")},
                      now() - interval '1 day', '{system}')
                 """);

            await InsertHistoryAsync(identityId, current.Hash, minutesAgo: 60 * 24);
        }

        return new Subject(new UserId(userId), identityId);
    }

    private async Task<Guid> AddExternalIdentityAsync(UserId userId)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            $"""
             INSERT INTO user_identity
                 (id, user_id, actor_type, identity_type, identity_provider,
                  subject_id, username, status, created_at, created_by)
             VALUES
                 ('{id}', '{userId.Value}', 'Human', 'External', 'EntraId',
                  'external-{id:N}', NULL, 'Active',
                  now() - interval '1 day', '{User.SystemUserId.Value}')
             """);

        return id;
    }

    /// <summary>
    /// A session in a chosen state, with timestamps from the application clock.
    /// </summary>
    private async Task<Guid> InsertSessionAsync(Guid identityId, SessionKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        var (created, lastActivity, expires) = kind switch
        {
            SessionKind.Active or SessionKind.Revoked =>
                (now.AddHours(-1), now.AddMinutes(-1), now.AddHours(8)),

            // Past the idle timeout, inside the tolerance: still accepted.
            SessionKind.IdleWithinTolerance =>
                (now.AddHours(-1), now - Idle - TimeSpan.FromSeconds(20), now.AddHours(8)),

            // Past the idle timeout AND the tolerance: already refused.
            SessionKind.IdlePastTolerance =>
                (now.AddHours(-1), now - Idle - Tolerance - TimeSpan.FromMinutes(2), now.AddHours(8)),

            SessionKind.Expired =>
                (now.AddDays(-2), now.AddDays(-2), now.AddDays(-1)),

            // Inside the idle window, past absolute expiry: only expiry refuses it.
            SessionKind.ExpiredRecentlyActive =>
                (now.AddHours(-1), now.AddSeconds(-30), now.AddSeconds(-10)),

            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_session
                (id, user_identity_id, created_at, last_activity_at, expires_at,
                 revoked_at, revoked_by, revocation_reason, ip_address, user_agent)
            VALUES
                (@id, @identity, @created, @lastActivity, @expires,
                 @revokedAt, @revokedBy, @reason, NULL, 'change-password-tests/1.0')
            """, connection);

        var revoked = kind == SessionKind.Revoked;

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identity", identityId);
        command.Parameters.AddWithValue("created", created);
        command.Parameters.AddWithValue("lastActivity", lastActivity);
        command.Parameters.AddWithValue("expires", expires);
        command.Parameters.AddWithValue("revokedAt", revoked ? now.AddMinutes(-10) : DBNull.Value);
        command.Parameters.AddWithValue("revokedBy", revoked ? User.SystemUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("reason", revoked ? "Logout" : DBNull.Value);

        await command.ExecuteNonQueryAsync();

        return id;
    }

    private async Task InsertHistoryAsync(Guid identityId, string hash, int minutesAgo)
        => await ExecuteAsync(
            $"""
             INSERT INTO password_history
                 (id, user_identity_id, password_hash, password_algorithm, created_at)
             VALUES
                 ('{Guid.NewGuid()}', '{identityId}', '{hash}', 'pbkdf2-sha256-v1',
                  now() - interval '{minutesAgo} minutes')
             """);

    private async Task<CredentialRow> ReadCredentialAsync(Guid identityId)
        => await TryReadCredentialAsync(identityId)
           ?? throw new InvalidOperationException("The credential row is missing.");

    private async Task<CredentialRow?> TryReadCredentialAsync(Guid identityId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT password_hash, password_algorithm, password_changed_at::text,
                   must_change_password, failed_attempt_count, locked_until IS NOT NULL
            FROM credential WHERE user_identity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new CredentialRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetBoolean(3), reader.GetInt32(4), reader.GetBoolean(5));
    }

    private async Task<SessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT revoked_at IS NOT NULL, revoked_by, revocation_reason FROM user_session WHERE id = @id",
            connection);

        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "The session row is missing.");

        return new SessionRow(
            reader.GetBoolean(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<IReadOnlyList<ChangeRecord>> ReadChangeRecordsAsync(Guid identityId)
    {
        var rows = new List<ChangeRecord>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT r.audit_id, r.origin_kind, r.actor_user_id, r.entity_type, r.payload::text,
                   r.payload::jsonb ->> 'algorithm',
                   r.payload::jsonb ->> 'otherSessionsRevoked'
            FROM audit.audit_record r
            JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
            WHERE r.event_type = 'PasswordChanged'
              AND e.entity_type = 'Identity' AND e.entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", identityId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ChangeRecord(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<RevocationRecord>> ReadRevocationRecordsAsync(Guid sessionId)
    {
        var rows = new List<RevocationRecord>();

        await using var connection = await _database.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT origin_kind, actor_user_id, reason, causation_id
            FROM audit.audit_record
            WHERE event_type = 'SessionRevoked'
              AND entity_type = 'Session' AND entity_id = @id
            """, connection);

        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RevocationRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        }

        return rows;
    }

    private Task<long> CountRevocationsForIdentityAsync(Guid identityId)
        => CountRecordsAsync("SessionRevoked", identityId);

    private Task<long> CountRecordsAsync(string eventType, Guid identityId)
        => ScalarAsync<long>(
            $"""
             SELECT count(*) FROM audit.audit_record r
             JOIN audit.audit_entity_ref e ON e.audit_id = r.audit_id
             WHERE r.event_type = '{eventType}'
               AND e.entity_type = 'Identity' AND e.entity_id = '{identityId}'
             """);

    private Task<long> CountHistoryAsync(Guid identityId)
        => ScalarAsync<long>($"SELECT count(*) FROM password_history WHERE user_identity_id = '{identityId}'");

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }
}
