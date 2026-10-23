using System.Globalization;

using Poly.Ast.Nodes;

namespace Poly.DslCompiler;

/// <summary>
/// The two <c>[Range]</c> argument constants for a numeric DTO member, and the single place the
/// constructor choice is made. <see cref="System.ComponentModel.DataAnnotations.RangeAttribute"/>
/// offers only <c>(int, int)</c> and <c>(double, double)</c>, so an integral target whose bounds are
/// both integral and within <see cref="int"/> range takes the exact integral constructor; everything
/// else — a fractional bound, a bound beyond <see cref="int"/>, or a floating-point target — takes
/// the double constructor deliberately. Open bounds cap at the CLR type's own limits, so an open
/// range on a small integral type can still be integral.
/// </summary>
internal static class RangeAttributeArguments {
    /// <summary>The minimum and maximum arguments, in order, for <paramref name="clrType"/>.</summary>
    public static (Constant Minimum, Constant Maximum) Build(
        string clrType, object? minimum, object? maximum) {
        var (openMinimum, openMaximum) = ClrNumericBounds(clrType);
        var min = minimum ?? openMinimum;
        var max = maximum ?? openMaximum;
        if (IsIntegralClrType(clrType) && TryInt32(min, out var low) && TryInt32(max, out var high))
            return (new Constant(low), new Constant(high));
        return (new Constant(ToDouble(min)), new Constant(ToDouble(max)));
    }

    private static bool IsIntegralClrType(string clrType) =>
        clrType is "byte" or "short" or "int" or "long";

    /// <summary>The bound as an <see cref="int"/>, when it is integral and fits — the only shape the
    /// integral <c>RangeAttribute</c> constructor can carry.</summary>
    private static bool TryInt32(object value, out int result) {
        result = 0;
        switch (value) {
            case sbyte sb: result = sb; return true;
            case byte b: result = b; return true;
            case short s: result = s; return true;
            case ushort us: result = us; return true;
            case int i: result = i; return true;
            case uint ui when ui <= int.MaxValue: result = (int)ui; return true;
            case long l when l is >= int.MinValue and <= int.MaxValue: result = (int)l; return true;
            case ulong ul when ul <= int.MaxValue: result = (int)ul; return true;
            case float f: return TryInt32((double)f, out result);
            case double d when d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                result = (int)d;
                return true;
            case decimal m when decimal.Truncate(m) == m && m is >= int.MinValue and <= int.MaxValue:
                result = (int)m;
                return true;
            default: return false;
        }
    }

    /// <summary>The CLR type's own representable bounds, carried in that type — the cap an open
    /// range bound falls back to. The DTO member can never exceed it.</summary>
    private static (object Minimum, object Maximum) ClrNumericBounds(string clrType) => clrType switch {
        "byte" => (byte.MinValue, byte.MaxValue),
        "short" => (short.MinValue, short.MaxValue),
        "int" => (int.MinValue, int.MaxValue),
        "long" => (long.MinValue, long.MaxValue),
        "float" => (float.MinValue, float.MaxValue),
        "decimal" => (decimal.MinValue, decimal.MaxValue),
        _ => (double.MinValue, double.MaxValue),
    };

    private static double ToDouble(object value) => value switch {
        double d => d,
        float f => f,
        decimal m => (double)m,
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };
}