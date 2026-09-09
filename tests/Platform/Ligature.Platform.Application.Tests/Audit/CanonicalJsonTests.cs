using System.Text.Json;
using Ligature.Platform.Application.Audit;

namespace Ligature.Platform.Application.Tests.Audit;

/// <summary>
/// IMPL-03. The Design Specification (section 17.2) is explicit that RFC
/// 8785 is not key sorting alone, and lists what the suite must assert:
/// identical bytes for the same semantic value given different key insertion
/// order, different insignificant whitespace, nested objects, arrays of
/// objects, and the integer/decimal edge cases — 1 vs 1.0, exponents, −0,
/// large integers — and rejection of non-I-JSON input. Each is here.
/// </summary>
public sealed class CanonicalJsonTests
{
    [Fact]
    public void Key_insertion_order_does_not_change_the_bytes()
    {
        var a = CanonicalJson.Canonicalize(JsonDocument.Parse("""{"b":1,"a":2,"c":3}""").RootElement);
        var b = CanonicalJson.Canonicalize(JsonDocument.Parse("""{"c":3,"a":2,"b":1}""").RootElement);

        Assert.Equal(a, b);
        Assert.Equal("""{"a":2,"b":1,"c":3}""", a);
    }

    [Fact]
    public void Insignificant_whitespace_does_not_change_the_bytes()
    {
        var compact = CanonicalJson.Canonicalize(JsonDocument.Parse("""{"a":[1,2],"b":{"c":true}}""").RootElement);
        var spaced = CanonicalJson.Canonicalize(JsonDocument.Parse("""
            {
              "b" : { "c" : true },
              "a" : [ 1 , 2 ]
            }
            """).RootElement);

        Assert.Equal(compact, spaced);
    }

    [Fact]
    public void Nested_objects_and_arrays_of_objects_are_canonicalised_throughout()
    {
        var text = CanonicalJson.Canonicalize(JsonDocument.Parse(
            """{"z":{"y":{"x":1,"w":2}},"list":[{"b":1,"a":2},{"d":[{"f":1,"e":2}]}]}""").RootElement);

        Assert.Equal("""{"list":[{"a":2,"b":1},{"d":[{"e":2,"f":1}]}],"z":{"y":{"w":2,"x":1}}}""", text);
    }

    /// <summary>RFC 8785 sorts by UTF-16 code units, so uppercase precedes lowercase and 'Z' precedes 'a'.</summary>
    [Fact]
    public void Keys_sort_by_UTF16_code_units_not_by_locale()
    {
        var text = CanonicalJson.Canonicalize(JsonDocument.Parse("""{"a":1,"Z":2,"B":3}""").RootElement);

        Assert.Equal("""{"B":3,"Z":2,"a":1}""", text);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1.0", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("-0", "0")]
    [InlineData("-0.0", "0")]
    [InlineData("1e2", "100")]
    [InlineData("1E+2", "100")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("0.0000001", "1e-7")]
    [InlineData("1.5e-7", "1.5e-7")]
    [InlineData("123456789012345", "123456789012345")]
    [InlineData("9007199254740992", "9007199254740992")]
    [InlineData("-9007199254740992", "-9007199254740992")]
    [InlineData("0.1", "0.1")]
    [InlineData("-2.5", "-2.5")]
    public void Numbers_take_the_ES6_form(string input, string expected)
    {
        var text = CanonicalJson.Canonicalize(JsonDocument.Parse(input).RootElement);

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Integers beyond 2^53 have already lost precision in any reader; I-JSON
    /// forbids them and a canonical form that rounds is not canonical. The
    /// refusal is the answer even where the value happens to be exact as a
    /// double (1e20), because the reader on the other side is what decides
    /// whether the trail still says what was written.
    ///
    /// 2^53 + 1 is the case a double comparison would let through: it rounds
    /// to 2^53 before the bound is applied.
    /// </summary>
    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("18446744073709551616")]
    [InlineData("-9007199254740993")]
    [InlineData("1e20")]
    [InlineData("1e21")]
    public void Integers_beyond_the_safe_range_are_refused(string input)
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => CanonicalJson.Canonicalize(JsonDocument.Parse(input).RootElement));

        Assert.Contains("I-JSON", failure.Message);
    }

    /// <summary>
    /// ES6 escapes only the quote, the backslash and control characters;
    /// everything else — the solidus, non-ASCII — is literal. System.Text.Json's
    /// default encoder escapes more, which is why strings are written here
    /// rather than delegated.
    /// </summary>
    [Fact]
    public void Strings_use_ES6_escaping()
    {
        var text = CanonicalJson.Serialize(new { s = "a\"b\\c/dé\n" });

        Assert.Equal("{\"s\":\"a\\\"b\\\\c/dé\\n\\u0001\"}", text);
    }

    [Fact]
    public void Serialising_an_object_keeps_declared_property_names()
    {
        // The catalogue's PII paths are 'After.FirstName' etc.; a naming
        // policy that camel-cased them would point the paths at nothing.
        var text = CanonicalJson.Serialize(new { FirstName = "Ada", Email = (string?)null });

        Assert.Equal("""{"Email":null,"FirstName":"Ada"}""", text);
    }

    [Fact]
    public void The_same_value_serialised_twice_is_byte_identical()
    {
        var value = new { b = new[] { 3, 2, 1 }, a = new { y = 1.0, x = "x" } };

        Assert.Equal(CanonicalJson.Serialize(value), CanonicalJson.Serialize(value));
    }
}
