using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Users.Queries.UserList;

/// <summary>
/// USR-Q1. Specific to this query, deliberately: there is no generic paged
/// result, because the first paginated read is not evidence that a second will
/// want the same shape.
///
/// No total. HasMore is all paging needs, and a count would disclose the
/// tenant's population to every holder of user.read.
/// </summary>
/// <param name="PageSize">The size applied, which is the default when the caller supplied none.</param>
public sealed record UsersResult(
    IReadOnlyList<UserListRow> Users,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>
/// A user-list row: exactly these four fields (P1, as amended). UserId is the
/// only identifier; DisplayName and Email are presentation, and a client must
/// not key, route, cache or match on either (P4).
/// </summary>
/// <param name="Email">
/// Nullable because the column is. Every creation path supplies an address,
/// but nothing in the database requires one, and the row does not claim a
/// guarantee the schema does not make.
/// </param>
/// <param name="ActivationPending">
/// Amendment 1, presentation data: true when the user holds a local identity
/// and no credential. False means only "not pending", never "activated" or
/// "can sign in". It authorises nothing; CRD-C7 and CRD-C5 decide by their
/// own rules.
/// </param>
public sealed record UserListRow(
    UserId UserId,
    string DisplayName,
    string? Email,
    bool ActivationPending,
    UserStatus Status);
