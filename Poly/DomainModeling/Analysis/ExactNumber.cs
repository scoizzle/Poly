using System.Globalization;
using System.Numerics;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Exact analysis-time number. Decimal-backed, so every <see cref="long"/> and every
/// <see cref="decimal"/> is exact; arithmetic returns <c>null</c> instead of a rounded or wrapped
/// result, letting a caller widen an interval to unbounded (OQ-6, #643).
/// </summary>
/// <remarks>
/// A result is inexact when decimal would round it: more than 96 mantissa bits after removing
/// trailing zeros, or a scale above 28. That is reported as <c>null</c>, the same as overflow.
/// </remarks>
internal readonly struct ExactNumber : IEquatable<ExactNumber> {
    private static readonly BigInteger DecimalMaxMantissa = (BigInteger.One << 96) - 1;
    private const int DecimalMaxScale = 28;
    private const double ExactDoubleLimit = 9007199254740992d; // 2^53

    private ExactNumber(decimal value) => Value = value;

    /// <summary>The exact decimal this number stands for.</summary>
    public decimal Value { get; }

    /// <summary>Converts a CLR numeric value, or returns null when it is not exactly representable.</summary>
    /// <remarks>
    /// A <see cref="double"/> is accepted only when it is finite, integral, and within ±2^53 — the
    /// range in which every integer has a unique double. A larger or fractional double is unknown.
    /// </remarks>
    public static ExactNumber? TryFrom(object? value) => value switch {
        null => null,
        ExactNumber exact => exact,
        long l => new ExactNumber(l),
        int i => new ExactNumber(i),
        short s => new ExactNumber(s),
        sbyte sb => new ExactNumber(sb),
        byte b => new ExactNumber(b),
        ushort us => new ExactNumber(us),
        uint ui => new ExactNumber(ui),
        ulong ul => ul <= long.MaxValue ? new ExactNumber((long)ul) : null,
        decimal m => new ExactNumber(m),
        double d => FromDouble(d),
        float f => FromDouble(f),
        _ => null,
    };

    private static ExactNumber? FromDouble(double value) {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || Math.Abs(value) > ExactDoubleLimit)
            return null;
        return new ExactNumber((long)value);
    }

    /// <summary>
    /// Parses unsigned decimal text (digits and at most one dot, no exponent) exactly, or returns null
    /// when decimal would round it (scale above 28 after trailing zeros, or a mantissa above 96 bits).
    /// <see cref="decimal.TryParse(string, out decimal)"/> alone rounds a bound like
    /// <c>0.00000000000000000000000000005</c> to zero, so a bound parser must not trust it (#643, D2).
    /// </summary>
    public static ExactNumber? TryParseDecimalText(string text) {
        ArgumentNullException.ThrowIfNull(text);
        var dot = text.IndexOf('.');
        var digits = dot < 0 ? text : string.Concat(text.AsSpan(0, dot), text.AsSpan(dot + 1));
        var scale = dot < 0 ? 0 : text.Length - dot - 1;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return null;
        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            return new ExactNumber(new decimal(0, 0, 0, false, (byte)(scale <= DecimalMaxScale ? scale : 0)));
        var mantissa = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        // Keep the declared scale whenever decimal can hold it; trim trailing zeros only to fit.
        // (The arithmetic path still normalises in FromMantissaScale; a parsed bound keeps its text.)
        if (scale > DecimalMaxScale || mantissa > DecimalMaxMantissa) {
            var end = digits.Length;
            while (end > 0 && scale > 0 && digits[end - 1] == '0') {
                end--;
                scale--;
            }
            mantissa = BigInteger.Parse(digits[..end], CultureInfo.InvariantCulture);
            if (scale > DecimalMaxScale || mantissa > DecimalMaxMantissa)
                return null;
        }
        return new ExactNumber(BuildDecimal(mantissa, scale));
    }

    /// <summary>Orders two CLR values exactly, or null when either side is not exactly representable.</summary>
    public static int? Compare(object? left, object? right) {
        if (TryFrom(left) is not { } l || TryFrom(right) is not { } r)
            return null;
        return l.Value.CompareTo(r.Value);
    }

    public ExactNumber? TryAdd(ExactNumber other) {
        var (a, scaleA) = Parts(Value);
        var (b, scaleB) = Parts(other.Value);
        var scale = Math.Max(scaleA, scaleB);
        var sum = (a * BigInteger.Pow(10, scale - scaleA)) + (b * BigInteger.Pow(10, scale - scaleB));
        return FromMantissaScale(sum, scale);
    }

    public ExactNumber? TrySubtract(ExactNumber other) {
        var (a, scaleA) = Parts(Value);
        var (b, scaleB) = Parts(other.Value);
        var scale = Math.Max(scaleA, scaleB);
        var difference = (a * BigInteger.Pow(10, scale - scaleA)) - (b * BigInteger.Pow(10, scale - scaleB));
        return FromMantissaScale(difference, scale);
    }

    public ExactNumber? TryMultiply(ExactNumber other) {
        var (a, scaleA) = Parts(Value);
        var (b, scaleB) = Parts(other.Value);
        return FromMantissaScale(a * b, scaleA + scaleB);
    }

    /// <summary>Negation is always exact; the nullable result keeps the arithmetic shape uniform.</summary>
    public ExactNumber? TryNegate() => new ExactNumber(-Value);

    public decimal ToDecimal() => Value;

    public bool TryToLong(out long value) {
        if (Value != decimal.Truncate(Value) || Value < long.MinValue || Value > long.MaxValue) {
            value = 0;
            return false;
        }
        value = (long)Value;
        return true;
    }

    public bool Equals(ExactNumber other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is ExactNumber other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Splits a decimal into its signed mantissa and scale (value = mantissa × 10^-scale).</summary>
    private static (BigInteger Mantissa, int Scale) Parts(decimal value) {
        var bits = decimal.GetBits(value);
        var negative = (bits[3] & unchecked((int)0x80000000)) != 0;
        var scale = (bits[3] >> 16) & 0xFF;
        var mantissa = (BigInteger)(uint)bits[0]
            | ((BigInteger)(uint)bits[1] << 32)
            | ((BigInteger)(uint)bits[2] << 64);
        return (negative ? -mantissa : mantissa, scale);
    }

    /// <summary>Builds an exact decimal from mantissa and scale, or null when decimal would round it.</summary>
    private static ExactNumber? FromMantissaScale(BigInteger mantissa, int scale) {
        if (mantissa.IsZero)
            return new ExactNumber(0m);
        while (scale > 0 && (mantissa % 10).IsZero) {
            mantissa /= 10;
            scale--;
        }
        if (scale < 0 || scale > DecimalMaxScale)
            return null;
        if (BigInteger.Abs(mantissa) > DecimalMaxMantissa)
            return null;
        return new ExactNumber(BuildDecimal(mantissa, scale));
    }

    /// <summary>Builds a decimal from a 96-bit-bounded mantissa and a 0..28 scale, without normalising.</summary>
    private static decimal BuildDecimal(BigInteger mantissa, int scale) {
        var negative = mantissa.Sign < 0;
        var magnitude = BigInteger.Abs(mantissa);
        var lo = (int)(uint)(magnitude & 0xFFFFFFFF);
        var mid = (int)(uint)((magnitude >> 32) & 0xFFFFFFFF);
        var hi = (int)(uint)((magnitude >> 64) & 0xFFFFFFFF);
        return new decimal(lo, mid, hi, negative, (byte)scale);
    }
}