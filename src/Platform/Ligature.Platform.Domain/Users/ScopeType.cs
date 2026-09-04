using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class ScopeType : IEquatable<ScopeType>
{
    private ScopeType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ScopeType Global
        => new("Global");

    public static ScopeType Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(
                "Scope type cannot be empty.");

        return new ScopeType(value);
    }

    public bool Equals(ScopeType? other)
        => other is not null &&
           string.Equals(
               Value,
               other.Value,
               StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj)
        => obj is ScopeType other && Equals(other);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString()
        => Value;
}