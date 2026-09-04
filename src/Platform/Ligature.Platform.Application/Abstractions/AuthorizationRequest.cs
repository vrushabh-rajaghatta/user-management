using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public sealed record AuthorizationRequest(
    UserId UserId,
    string PermissionCode,
    DateTimeOffset At,
    string ScopeType,
    Guid? ScopeId);