using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Users.Queries.UserProfile;

/// <summary>
/// USR-Q1 GetUser v2: exactly these seven fields. First and last name are
/// non-null for the human users this read serves (ck_app_user_human_names).
/// Email, Status and ActivationPending mean exactly what they mean on the
/// list row (USR-Q2): Email is nullable because the column is, and
/// ActivationPending false means only "not pending".
/// </summary>
public sealed record UserProfileResult(
    UserId UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? Email,
    UserStatus Status,
    bool ActivationPending);
