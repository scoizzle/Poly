using System.Globalization;

using Poly.DomainModeling.Analysis;
using Poly.Grammar;

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
    private static readonly string DecimalMaxDigits = ExactNumber.MaxMantissa.ToString(CultureInfo.InvariantCulture);

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

    // ── reading a number token's value: exact, or a positioned error (T-15) ─────────────

    private const int MaxShownLength = 40;

    /// <summary>The value of a Number token: a <see cref="long"/> when integral and in range, else an exact <see cref="decimal"/>; anything else is a positioned error.</summary>
    public static object ParseLiteral(DslToken token) {
        var text = token.Span;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            return whole;
        var value = ExactDecimalOrThrow(token);
        // 1.0 is integral. A fraction is decimal. Never a double.
        if (value == decimal.Truncate(value) && value >= long.MinValue && value <= long.MaxValue)
            return (long)value;
        return value;
    }

    /// <summary>A <c>length(...)</c> bound: a whole number from 0 to <see cref="int.MaxValue"/>, written without a decimal point.</summary>
    public static int ParseLengthBound(DslToken token) =>
        int.TryParse(token.Span, NumberStyles.None, CultureInfo.InvariantCulture, out var bound)
            ? bound
            : throw WholeNumberError(token, "Length bound", int.MaxValue.ToString(CultureInfo.InvariantCulture));

    /// <summary>A duration's amount: a whole number from 0 to <see cref="long.MaxValue"/>, written without a decimal point.</summary>
    public static long ParseDurationAmount(DslToken token) =>
        long.TryParse(token.Span, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw WholeNumberError(token, "Duration amount", long.MaxValue.ToString(CultureInfo.InvariantCulture));

    /// <summary>An annotation argument: the one literal rule (exact, or a positioned error), then the nearest <see cref="double"/>.</summary>
    public static double ParseAnnotationNumber(DslToken token) {
        ExactDecimalOrThrow(token);
        return double.Parse(token.Span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>The whole value of an integral range bound (<c>100.0</c> is whole), or false when it is fractional or outside <see cref="long"/>. A magnitude of 2^63 with a minus is <see cref="long.MinValue"/>.</summary>
    public static bool TryParseWholeInt64(ReadOnlySpan<char> digitText, bool negative, out long value) {
        if (TryExactDecimal(digitText, out var dec, out _)) {
            var signed = negative ? -dec : dec;
            if (dec == decimal.Truncate(dec) && signed >= long.MinValue && signed <= long.MaxValue) {
                value = (long)signed;
                return true;
            }
        }
        value = 0;
        return false;
    }

    private static decimal ExactDecimalOrThrow(DslToken token) {
        var text = token.Span;
        if (TryExactDecimal(text, out var value, out var fractionDigits))
            return value;
        throw GrammarError.Error(NotExactMessage(token, Classify(text), fractionDigits), token.Line, token.Col);
    }

    private static bool TryExactDecimal(ReadOnlySpan<char> text, out decimal value, out int fractionDigits) {
        Shape(text, out var integerDigits, out fractionDigits);
        // At most 28 significant digits is always exact: no allocation on the common path.
        if (integerDigits + fractionDigits <= ExactNumber.MaxScale
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            return true;
        if (ExactNumber.TryParseDecimalText(text.ToString()) is { } exact) {
            value = exact.ToDecimal();
            return true;
        }
        value = 0;
        return false;
    }

    // Significant digits either side of the point: leading zeros of the integer part and trailing zeros of the fraction are free.
    private static void Shape(ReadOnlySpan<char> text, out int integerDigits, out int fractionDigits) {
        var dot = text.IndexOf('.');
        var integral = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? ReadOnlySpan<char>.Empty : text[(dot + 1)..];
        integerDigits = integral.TrimStart('0').Length;
        fractionDigits = fraction.TrimEnd('0').Length;
    }

    // The limit printed is the limit enforced: the mantissa cap is decimal.MaxValue's, scaled by the digits after the point.
    private static string NotExactMessage(DslToken token, NumberLiteralKind kind, int fractionDigits) {
        var shown = Shown(token);
        var places = fractionDigits.ToString(CultureInfo.InvariantCulture);
        if (kind == NumberLiteralKind.IntegerOverflow)
            return $"Number '{shown}' is larger than the maximum {DecimalMaxDigits}.";
        if (fractionDigits > ExactNumber.MaxScale)
            return $"Number '{shown}' has {places} digits after the point; the maximum is {ExactNumber.MaxScale.ToString(CultureInfo.InvariantCulture)}.";
        var scaledMax = fractionDigits == 0
            ? DecimalMaxDigits
            : DecimalMaxDigits[..^fractionDigits] + "." + DecimalMaxDigits[^fractionDigits..];
        var noun = fractionDigits == 1 ? "digit" : "digits";
        return $"Number '{shown}' is larger than the maximum {scaledMax} for {places} {noun} after the point.";
    }

    private static FormatException WholeNumberError(DslToken token, string what, string maximum) {
        var written = token.Span.Contains('.') ? ", written without a decimal point" : "";
        return GrammarError.Error($"{what} '{Shown(token)}' must be a whole number from 0 to {maximum}{written}.", token.Line, token.Col);
    }

    private static string Shown(DslToken token) {
        var shown = DslTokenText.Shown(token);
        return shown.Length <= MaxShownLength ? shown : string.Concat(shown.AsSpan(0, MaxShownLength - 3), "...");
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