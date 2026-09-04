using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class IdentityProvider : IEquatable<IdentityProvider>
{
    private IdentityProvider(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static IdentityProvider Application
        => new("Application");

    public static IdentityProvider Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(
                "Identity provider cannot be empty.");

        return new IdentityProvider(value);
    }

    public bool Equals(IdentityProvider? other)
        => other is not null &&
           string.Equals(
               Value,
               other.Value,
               StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj)
        => obj is IdentityProvider other &&
           Equals(other);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString()
        => Value;
}