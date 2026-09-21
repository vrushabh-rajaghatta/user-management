using SKSMCorp.Platform.Domain.Users;

namespace SKSMCorp.Platform.Application.Abstractions;

public sealed record AuthorizationRequest(
    UserId UserId,
    string PermissionCode,
    DateTimeOffset At,
    string ScopeType,
    Guid? ScopeId);