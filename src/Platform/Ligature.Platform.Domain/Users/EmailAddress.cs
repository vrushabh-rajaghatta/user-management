using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Domain.Users;

/// <summary>
/// An email address, in the shape this system is prepared to accept rather than
/// the full RFC 5322 grammar. The checks are deliberately structural — length
/// bounds, one @ in a sensible place, a dotted domain — because a stricter
/// parser would reject deliverable addresses and a looser one would accept
/// obvious rubbish.
///
/// ONE OF THE RULES IS A SECURITY BOUNDARY, not a shape check: the value may
/// contain no control character (U+0000-U+001F, U+007F-U+009F). An address is
/// interpolated into a mail header when an activation link is sent, and an
/// embedded CR or LF there adds headers of an attacker's choosing or terminates
/// the header block and replaces the body. The same value also reaches the audit
/// trail and the caller context, where a malformed address would be recorded
/// permanently.
///
/// That rule is mirrored exactly by a CHECK constraint on app_user.email, so it
/// holds for values arriving by any route rather than only through this type.
/// char.IsControl and the constraint's character ranges were verified to agree.
/// PostgreSQL additionally refuses U+0000 at the text encoding boundary, before
/// any constraint is evaluated — a separate mechanism, not this one.
/// </summary>
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

        // AFTER the trim, on the trimmed value, and the order matters. Trim
        // already removes surrounding whitespace — including tab, CR, LF and
        // NEL — and that has always been accepted, so checking first would
        // silently narrow the contract. Checking here makes the guarantee one
        // about the resulting Value: whatever a caller ends up holding carries
        // no control character. Non-whitespace controls are not trimmed and are
        // refused wherever they appear.
        if (value.Any(char.IsControl))
            return null;

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