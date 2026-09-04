using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

public sealed class EmailAddress : IEquatable<EmailAddress>
{
    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmailAddress Create(string raw)
    {
        return TryCreate(raw)
            ?? throw new DomainException(
                "Email address has an invalid format.");
    }

    public static EmailAddress? TryCreate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        if (value.Length > 254)
            return null;

        var atIndex = value.IndexOf('@');

        if (atIndex <= 0)
            return null;

        if (atIndex != value.LastIndexOf('@'))
            return null;

        if (atIndex > 64)
            return null;

        if (atIndex == value.Length - 1)
            return null;

        var domain = value[(atIndex + 1)..];

        if (!domain.Contains('.'))
            return null;

        return new EmailAddress(value);
    }

    public bool Equals(EmailAddress? other)
    {
        return other is not null &&
               string.Equals(
                   Value,
                   other.Value,
                   StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return obj is EmailAddress other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
    }

    public override string ToString()
        => Value;
}