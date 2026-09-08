using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Tests;

/// <summary>
/// The lockout state machine and the rehash operation (SES-C1).
///
/// Both are places where a plausible shortcut is wrong: reusing ChangePassword
/// for a rehash, or letting an expired lock keep its counter. These tests state
/// the intended transitions so neither can be reintroduced as a tidy-up.
/// </summary>
public sealed class CredentialTests
{
    private static readonly DateTimeOffset Created =
        new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------- rehashing

    /// <summary>
    /// A rehash is the SAME password in a newer encoding. PasswordChangedAt
    /// "drives expiry policy if introduced", so moving it would hand every user
    /// an indefinite extension the moment the algorithm changed.
    /// </summary>
    [Fact]
    public void Rehashing_replaces_the_representation_and_nothing_else()
    {
        var credential = NewCredential();

        credential.RecordFailedAttempt();
        credential.RecordFailedAttempt();
        credential.Lock(Created.AddMinutes(15));

        credential.RehashPassword("$new-hash", "pbkdf2-sha256-v2");

        Assert.Equal("$new-hash", credential.PasswordHash);
        Assert.Equal("pbkdf2-sha256-v2", credential.PasswordAlgorithm);

        // Untouched: the password did not change, and clearing the lock is the
        // successful-sign-in transition, not a consequence of re-encoding.
        Assert.Equal(Created, credential.PasswordChangedAt);
        Assert.Equal(2, credential.FailedAttemptCount);
        Assert.Equal(Created.AddMinutes(15), credential.LockedUntil);
    }

    /// <summary>
    /// The distinction that makes RehashPassword worth having at all.
    /// </summary>
    [Fact]
    public void Changing_a_password_moves_the_timestamp_but_rehashing_does_not()
    {
        var changed = NewCredential();
        changed.ChangePassword("$h", "alg", Created.AddDays(30), false);

        Assert.Equal(Created.AddDays(30), changed.PasswordChangedAt);

        var rehashed = NewCredential();
        rehashed.RehashPassword("$h", "alg");

        Assert.Equal(Created, rehashed.PasswordChangedAt);
    }

    [Theory]
    [InlineData("", "alg")]
    [InlineData("   ", "alg")]
    [InlineData("$hash", "")]
    [InlineData("$hash", "   ")]
    public void Rehashing_rejects_empty_material(string hash, string algorithm)
    {
        Assert.Throws<DomainException>(
            () => NewCredential().RehashPassword(hash, algorithm));
    }

    // -------------------------------------------------------- lockout window

    [Fact]
    public void Failed_attempts_accumulate_without_locking()
    {
        var credential = NewCredential();

        credential.RecordFailedAttempt();
        credential.RecordFailedAttempt();

        Assert.Equal(2, credential.FailedAttemptCount);
        Assert.Null(credential.LockedUntil);
    }

    [Fact]
    public void Locking_records_the_moment_the_window_ends()
    {
        var credential = NewCredential();
        var until = Created.AddMinutes(15);

        credential.Lock(until);

        Assert.Equal(until, credential.LockedUntil);
    }

    /// <summary>
    /// Step 6's transition: success clears the count and the lock together.
    /// Leaving the count behind would put a just-authenticated account one
    /// mistake away from another lockout.
    /// </summary>
    [Fact]
    public void Unlocking_clears_the_lock_and_the_counter_together()
    {
        var credential = NewCredential();

        credential.RecordFailedAttempt();
        credential.RecordFailedAttempt();
        credential.Lock(Created.AddMinutes(15));

        credential.Unlock();

        Assert.Null(credential.LockedUntil);
        Assert.Equal(0, credential.FailedAttemptCount);
    }

    /// <summary>
    /// The state machine the handler relies on when it observes an EXPIRED
    /// lock: unlocking first means the next failure starts a fresh window at 1,
    /// rather than re-locking instantly because the counter never decayed.
    /// </summary>
    [Fact]
    public void A_failure_after_unlocking_starts_a_new_window_at_one()
    {
        var credential = NewCredential();

        for (var i = 0; i < 5; i++)
            credential.RecordFailedAttempt();

        credential.Lock(Created.AddMinutes(15));

        // What the handler does when it finds the lock expired.
        credential.Unlock();
        credential.RecordFailedAttempt();

        Assert.Equal(1, credential.FailedAttemptCount);
        Assert.Null(credential.LockedUntil);
    }

    private static Credential NewCredential()
        => Credential.Create(
            CredentialId.New(),
            UserIdentityId.New(),
            IdentityType.Local,
            "$pbkdf2-sha256$i=210000$c2FsdA==$a2V5",
            "pbkdf2-sha256-v1",
            passwordChangedAt: Created,
            mustChangePassword: false,
            createdAt: Created,
            createdBy: User.SystemUserId);
}
