namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// One permission a caller effectively holds, and the scope it was granted in.
///
/// The scope travels with it because a permission in one scope says nothing
/// about another. V1 assigns only Global with no id; the shape describes the
/// general effective set rather than the seed data's current shape.
/// </summary>
public sealed record EffectivePermission(
    string Code,
    string ScopeType,
    Guid? ScopeId);
