using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ligature.Platform.Application.Audit;

/// <summary>
/// RFC 8785 (JSON Canonicalization Scheme) for Before, After and Payload
/// (IMPL-03).
///
/// Two exports of the same closed range must produce byte-identical streams
/// and therefore the same digest (AUD-D35), and PostgreSQL's jsonb does not
/// preserve key order. So the content is canonicalised before it is written
/// and again when it is exported, and this type is the one definition of
/// "canonical" both use. RFC 8785 is not key sorting alone: it is sorted
/// keys (by UTF-16 code unit), no insignificant whitespace, ES6 string
/// escaping, and ES6 number formatting — 1.0 and 1 are the same number and
/// serialise the same way; −0 is 0; 1e21 is written in exponent form and
/// 1e20 is not.
///
/// Input is restricted to I-JSON (RFC 7493): numbers must be finite and
/// integers must be within ±2^53. Anything else is refused rather than
/// approximated, because a canonical form that rounds is not canonical.
///
/// Implemented here rather than taken as a dependency, in line with the
/// repository's stance on dependencies (docs/architecture.md section 18),
/// and because the ES6 number algorithm is small enough to own and large
/// enough to want tests for.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // Keys are written as declared. A naming policy would silently
        // rename the paths the catalogue's PiiPaths point at.
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private const long MaxSafeInteger = 9007199254740992L; // 2^53

    /// <summary>Serialises any value to its canonical text.</summary>
    public static string Serialize(object? value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);

        return Canonicalize(element);
    }

    /// <summary>The element form, for validators that inspect content.</summary>
    public static JsonElement ToElement(object? value)
        => JsonSerializer.SerializeToElement(value, Options);

    public static string Canonicalize(JsonElement element)
    {
        var builder = new StringBuilder();

        Write(element, builder);

        return builder.ToString();
    }

    private static void Write(JsonElement element, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');

                var first = true;

                // RFC 8785 section 3.2.3: properties sorted by the UTF-16
                // code units of their names, which is what ordinal comparison
                // of .NET strings compares.
                foreach (var property in element.EnumerateObject()
                             .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    if (!first) output.Append(',');
                    first = false;

                    WriteString(property.Name, output);
                    output.Append(':');
                    Write(property.Value, output);
                }

                output.Append('}');
                break;

            case JsonValueKind.Array:
                output.Append('[');

                var firstItem = true;

                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) output.Append(',');
                    firstItem = false;

                    Write(item, output);
                }

                output.Append(']');
                break;

            case JsonValueKind.String:
                WriteString(element.GetString()!, output);
                break;

            case JsonValueKind.Number:
                WriteNumber(element.GetRawText(), output);
                break;

            case JsonValueKind.True:
                output.Append("true");
                break;

            case JsonValueKind.False:
                output.Append("false");
                break;

            case JsonValueKind.Null:
                output.Append("null");
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot canonicalise a JSON value of kind {element.ValueKind}.");
        }
    }

    /// <summary>
    /// ES6 JSON.stringify escaping (RFC 8785 section 3.2.2.2): only the
    /// quotation mark, the backslash and control characters below U+0020
    /// are escaped; everything else, including non-ASCII and the solidus, is
    /// written literally.
    /// </summary>
    private static void WriteString(string value, StringBuilder output)
    {
        output.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                default:
                    if (c < ' ')
                        output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        output.Append(c);
                    break;
            }
        }

        output.Append('"');
    }

    /// <summary>
    /// ES6 Number::toString (RFC 8785 section 3.2.2.3), on the shortest
    /// round-trip digits .NET produces.
    /// </summary>
    /// <summary>
    /// True for a JSON number written as digits alone — no fraction, no
    /// exponent — which is the spelling whose value must survive exactly.
    /// </summary>
    private static bool IsIntegerLiteral(string raw)
        => !raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E');

    private static void WriteNumber(string raw, StringBuilder output)
    {
        // Integers within the safe range pass through with only sign and
        // zero normalisation; everything else goes through the double path.
        //
        // The bound is compared on the long, not on (double)long: 2^53 + 1
        // rounds to 2^53 as a double, so a double comparison would admit the
        // first value the range exists to exclude.
        if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            && integer >= -MaxSafeInteger && integer <= MaxSafeInteger)
        {
            output.Append(integer == 0 ? "0" : integer.ToString(CultureInfo.InvariantCulture));
            return;
        }

        // An integer literal that did not take the fast path is outside the
        // safe range, whether it overflowed long or merely exceeded 2^53.
        // Refusing it here rather than in the double path below matters:
        // 2^53 + 1 parses to a double equal to 2^53, so the magnitude test
        // there would find nothing wrong and silently write 2^53 back.
        // A canonical form that changes the value is not canonical.
        if (IsIntegerLiteral(raw))
        {
            throw new InvalidOperationException(
                $"'{raw}' exceeds the I-JSON safe integer range (±2^53) and cannot be canonicalised.");
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number))
        {
            throw new InvalidOperationException(
                $"'{raw}' is not a finite JSON number and cannot be canonicalised.");
        }

        if (number == 0)
        {
            // ES6: −0 serialises as "0".
            output.Append('0');
            return;
        }

        if (Math.Abs(number) > MaxSafeInteger && number == Math.Floor(number))
        {
            // The same rule for a value spelled with an exponent or a
            // fraction — 1e20, 1.0e20 — which reaches an integer beyond 2^53
            // by a different route and loses precision in the same readers.
            throw new InvalidOperationException(
                $"'{raw}' exceeds the I-JSON safe integer range (±2^53) and cannot be canonicalised.");
        }

        // Shortest round-trip digits, then re-laid out by the ES6 rules. The
        // "R" text is parsed for its digits and decimal exponent rather than
        // used as-is, because .NET's exponent notation ("1E-07") is not
        // ES6's ("1e-7") and its fixed/exponent threshold differs.
        var shortest = number.ToString("R", CultureInfo.InvariantCulture);
        var (negative, digits, exponent) = Decompose(shortest);

        if (negative) output.Append('-');

        var k = digits.Length;
        var n = exponent; // value = 0.d1...dk × 10^n, i.e. digits × 10^(n-k)

        if (k <= n && n <= 21)
        {
            output.Append(digits).Append('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            output.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
        }
        else if (-6 < n && n <= 0)
        {
            output.Append("0.").Append('0', -n).Append(digits);
        }
        else
        {
            output.Append(digits[0]);
            if (k > 1) output.Append('.').Append(digits, 1, k - 1);
            output.Append('e').Append(n - 1 >= 0 ? '+' : '-')
                  .Append(Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Splits a .NET round-trip string into (sign, significant digits with
    /// no leading or trailing zeros, decimal exponent n) such that the value
    /// is 0.digits × 10^n.
    /// </summary>
    private static (bool Negative, string Digits, int Exponent) Decompose(string text)
    {
        var negative = text.StartsWith('-');
        if (negative) text = text[1..];

        var exponentPart = 0;
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponentPart = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var point = text.IndexOf('.');
        var integerPart = point >= 0 ? text[..point] : text;
        var fractionPart = point >= 0 ? text[(point + 1)..] : string.Empty;

        var allDigits = (integerPart + fractionPart).TrimStart('0');
        var leadingZerosStripped = (integerPart + fractionPart).Length - allDigits.Length;

        // n counts the digits before the decimal point in the un-normalised
        // form, adjusted for stripped leading zeros and the exponent.
        var n = integerPart.Length - leadingZerosStripped + exponentPart;

        var digits = allDigits.TrimEnd('0');

        if (digits.Length == 0)
            digits = "0";

        return (negative, digits, n);
    }
}
