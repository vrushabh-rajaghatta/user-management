using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// A plausible actor identity for tests that need one but are not about it.
///
/// The values are deliberately unremarkable. A test asserting something else
/// should not have to decide what a display name looks like, and a shared
/// default keeps the interesting cases — a null username, an email on a
/// non-human actor — visibly deliberate where they appear.
/// </summary>
internal static class TestActorIdentity
{
    private static readonly DateTimeOffset Captured =
        new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    internal static ActorIdentity Human(string label = "Test Person")
        => new(
            DisplayName: label,
            Username: "test.person",
            Email: EmailAddress.Create("test.person@example.test"),
            IdentityProvider: IdentityProvider.Application,
            SubjectId: "test-subject",
            CapturedAt: Captured);

    /// <summary>
    /// AR11 permits an email only for human actors, so this carries none.
    /// </summary>
    internal static ActorIdentity NonHuman(string label = "Test Agent")
        => new(
            DisplayName: label,
            Username: "test.agent",
            Email: null,
            IdentityProvider: IdentityProvider.Application,
            SubjectId: "test-agent-subject",
            CapturedAt: Captured);
}
