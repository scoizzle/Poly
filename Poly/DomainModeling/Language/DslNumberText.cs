using System.Globalization;

using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Language;

/// <summary>Which stored type a DSL digit text maps to (see <see cref="DslNumberText.Classify"/>).</summary>
internal enum NumberLiteralKind {
    /// <summary>Integral value that fits a <see cref="long"/> (the reader stores it as one; <c>100.0</c> is integral).</summary>
    Integer,

    /// <summary>A nonzero fraction that <see cref="decimal"/> holds exactly (the reader stores a <see cref="decimal"/>).</summary>
    Fractional,

    /// <summary>Integral value beyond <see cref="long"/>.</summary>
    IntegerOverflow,

    /// <summary>A nonzero fraction <see cref="decimal"/> would round (scale above 28 or a mantissa above 96 bits).</summary>
    FractionalOverflow,
}

/// <summary>
/// The one place the DSL printer turns a number into text: plain digits, invariant culture, never an exponent.
/// The DSL number token is digits with at most one dot and no exponent (the sign is a separate token), so
/// <c>1E+21</c> is not a number the reader can take (#531).
/// </summary>
/// <remarks>
/// A <see cref="double"/> or <see cref="float"/> prints its shortest round-trip digits, expanded to plain digits.
/// Negative zero prints <c>0</c> (the reader has no negative zero). A value the reader cannot hold at all fails
/// closed: NaN, the infinities, and a magnitude beyond <see cref="decimal.MaxValue"/> (the reader stores a
/// fraction as <see cref="decimal"/>). The reader still rounds or refuses a digit text finer than decimal's scale
/// (a double like <c>5e-324</c>); that is the reader's rule and is pinned in the tests, not changed here.
/// <para>A <see cref="float"/> prints its shortest FLOAT digits (<c>1e15f</c> is <c>1000000000000000</c>, not the widened double's <c>999999986991104</c>).</para>
/// <para>Negative zero prints <c>0</c>.</para>
/// <para>An integral double literal keeps <c>.0</c> and an integral double bound does not, as on master (the reader folds <c>3.0</c> to the integer 3 either way).</para>
/// </remarks>
internal static class DslNumberText {
    private const string LongMaxDigits = "9223372036854775807";
    private const string DecimalMaxDigits = "79228162514264337593543950335";

    /// <summary>True when <see cref="Format"/> accepts the value's type.</summary>
    public static bool IsNumber(object? value) => value is
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or double or float;

    /// <summary>
    /// Plain-digit DSL text for a numeric value: never an exponent. Fails closed on NaN, the infinities and a magnitude
    /// beyond <see cref="decimal.MaxValue"/>. Text finer than Decimal's scale is exact digits that the reader refuses today
    /// (T-15 makes that a positioned error).
    /// </summary>
    public static string Format(object? value) => value switch {
        null => throw new ArgumentNullException(nameof(value)),
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal =>
            ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        double d => ExpandExponent(d.ToString("R", CultureInfo.InvariantCulture)),
        float f => ExpandExponent(f.ToString("R", CultureInfo.InvariantCulture)),
        _ => throw new ArgumentException(
            $"Cannot print a value of type '{value.GetType().Name}' as a DSL number.", nameof(value)),
    };

    /// <summary>
    /// Says which stored type a digit text (no sign: the sign is a separate token) maps to. Text that is not digits
    /// with at most one interior dot throws.
    /// </summary>
    public static NumberLiteralKind Classify(ReadOnlySpan<char> digitText) {
        var text = digitText.ToString();
        var dot = text.IndexOf('.');
        var integral = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? "" : text[(dot + 1)..];
        var shapeOk = integral.Length > 0
            && integral.All(char.IsAsciiDigit)
            && (dot < 0 || (fraction.Length > 0 && fraction.All(char.IsAsciiDigit)));
        if (!shapeOk)
            throw new ArgumentException($"'{text}' is not DSL digit text (digits with at most one interior dot).", nameof(digitText));

        if (fraction.All(c => c == '0')) {
            var significant = integral.TrimStart('0');
            var fits = significant.Length < LongMaxDigits.Length
                || (significant.Length == LongMaxDigits.Length && string.CompareOrdinal(significant, LongMaxDigits) <= 0);
            return fits ? NumberLiteralKind.Integer : NumberLiteralKind.IntegerOverflow;
        }

        return ExactNumber.TryParseDecimalText(text) is null ? NumberLiteralKind.FractionalOverflow : NumberLiteralKind.Fractional;
    }

    private static string ExpandExponent(string shortest) {
        if (shortest is "NaN" or "Infinity" or "-Infinity")
            throw new InvalidOperationException($"Cannot print '{shortest}' as a DSL number: the DSL has no NaN or infinity.");

        var negative = shortest[0] == '-';
        var body = negative ? shortest[1..] : shortest;
        var e = body.IndexOfAny(['E', 'e']);
        var exponent = e < 0 ? 0 : int.Parse(body[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? body : body[..e];
        var dot = mantissa.IndexOf('.');
        var integral = dot < 0 ? mantissa : mantissa[..dot];
        var digits = dot < 0 ? mantissa : integral + mantissa[(dot + 1)..];
        var pointAt = integral.Length + exponent;

        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leading..];
        pointAt -= leading;
        digits = digits.TrimEnd('0');
        if (digits.Length == 0)
            return "0";

        string plain;
        if (pointAt <= 0) {
            plain = "0." + new string('0', -pointAt) + digits;
        }
        else if (pointAt >= digits.Length) {
            var whole = digits + new string('0', pointAt - digits.Length);
            if (whole.Length > DecimalMaxDigits.Length
                || (whole.Length == DecimalMaxDigits.Length && string.CompareOrdinal(whole, DecimalMaxDigits) > 0))
                throw new InvalidOperationException($"Cannot print '{shortest}' as a DSL number: it is beyond the largest number the DSL reader holds ({DecimalMaxDigits}).");
            plain = whole;
        }
        else {
            plain = digits[..pointAt] + "." + digits[pointAt..];
        }

        return negative ? "-" + plain : plain;
    }
}