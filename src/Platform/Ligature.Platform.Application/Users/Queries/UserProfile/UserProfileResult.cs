using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.UserProfile;

/// <summary>
/// COMPILE-ONLY STUB (red tests) for v2's three added fields.
/// USR-Q1 v1: exactly these four fields. First and last name are non-null for
/// the human users this read serves (ck_app_user_human_names).
/// </summary>
public sealed record UserProfileResult(
    UserId UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? Email,
    UserStatus Status,
    bool ActivationPending);
