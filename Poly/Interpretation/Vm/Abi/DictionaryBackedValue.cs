using System.Linq.Expressions;
using System.Reflection;

using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.Vm.Abi;

/// <summary>
/// Reading and writing a modeled member of a dictionary-backed instance: the VM's
/// storage representation for an <c>AstTypeDefinition</c>, not a fact about the type.
/// Provides type coercion so stored values (e.g. <c>int</c>) are correctly
/// converted to the declared member type (e.g. <c>long</c> for Number).
/// </summary>
internal static class DictionaryBackedValue {
    internal static readonly MethodInfo DictContainsKey =
        Ref<IDictionary<string, object?>>.Method(d => d.ContainsKey(""));
    private static readonly PropertyInfo DictItem =
        Ref<IDictionary<string, object?>>.Indexer(d => d[""]);
    internal static readonly MethodInfo DictGetItem = DictItem.GetGetMethod()!;
    internal static readonly MethodInfo DictSetItem = DictItem.GetSetMethod()!;

    private static readonly MethodInfo ConvertToInt64 =
        Ref.Method((Expression<Func<object, long>>)(o => Convert.ToInt64(o)));
    private static readonly MethodInfo ConvertToInt32 =
        Ref.Method((Expression<Func<object, int>>)(o => Convert.ToInt32(o)));
    private static readonly MethodInfo ConvertToInt16 =
        Ref.Method((Expression<Func<object, short>>)(o => Convert.ToInt16(o)));
    private static readonly MethodInfo ConvertToSByte =
        Ref.Method((Expression<Func<object, sbyte>>)(o => Convert.ToSByte(o)));
    private static readonly MethodInfo ConvertToDouble =
        Ref.Method((Expression<Func<object, double>>)(o => Convert.ToDouble(o)));
    private static readonly MethodInfo ConvertToSingle =
        Ref.Method((Expression<Func<object, float>>)(o => Convert.ToSingle(o)));
    private static readonly MethodInfo ConvertToDecimal =
        Ref.Method((Expression<Func<object, decimal>>)(o => Convert.ToDecimal(o)));
    private static readonly MethodInfo ConvertToBoolean =
        Ref.Method((Expression<Func<object, bool>>)(o => Convert.ToBoolean(o)));
    private static readonly MethodInfo ConvertToString =
        Ref.Method((Expression<Func<object, string?>>)(o => Convert.ToString(o)));
    private static readonly MethodInfo ConvertToChar =
        Ref.Method((Expression<Func<object, char>>)(o => Convert.ToChar(o)));
    private static readonly MethodInfo GuardCompatibleInfo =
        Ref.Method((Expression<Func<object?, ClrPrimitive, object>>)((raw, t) => GuardCompatible(raw, t)));

    /// <summary>
    /// Value used when a dictionary key is absent and the member has no constant
    /// default. A converted value type uses that conversion's zero.
    /// <see cref="System.Reflection.Missing"/> cannot be the other branch of a
    /// value-type read: unboxing the condition then casts the zero to <c>Missing</c>.
    /// </summary>
    internal static Expression MissingKeyFallback(ITypeDefinition targetType) {
        // An unset optional value is null. Nullable<T> has no primitive, so the
        // value-type zero below would not apply and Missing cannot convert to T?.
        if (targetType.GetRuntimeType() is { } runtime && Nullable.GetUnderlyingType(runtime) is not null)
            return Expression.Constant(null, typeof(object));
        var convert = ConvertMethod(targetType.GetClrPrimitive());
        if (convert is null || !convert.ReturnType.IsValueType)
            return Expression.Convert(Expression.Constant(System.Reflection.Missing.Value), typeof(object));
        var zero = Activator.CreateInstance(convert.ReturnType)!;
        return Expression.Convert(Expression.Constant(zero), typeof(object));
    }

    private static MethodInfo? ConvertMethod(ClrPrimitive? primitive) => primitive switch {
        ClrPrimitive.Int64 => ConvertToInt64,
        ClrPrimitive.Int32 => ConvertToInt32,
        ClrPrimitive.Int16 => ConvertToInt16,
        ClrPrimitive.Int8 => ConvertToSByte,
        ClrPrimitive.Float64 => ConvertToDouble,
        ClrPrimitive.Float32 => ConvertToSingle,
        ClrPrimitive.Decimal => ConvertToDecimal,
        ClrPrimitive.Boolean => ConvertToBoolean,
        ClrPrimitive.String => ConvertToString,
        ClrPrimitive.Char => ConvertToChar,
        _ => null
    };

    internal static Expression CoerceRead(Expression dictValue, ITypeDefinition targetType) {
        var convertMethod = ConvertMethod(targetType.GetClrPrimitive());
        if (convertMethod is not null) {
            // Fail loud on fundamentally wrong-typed raw values instead of silently
            // coercing (Convert.ToInt64(true) → 1, Convert.ToString(null) → "", etc.):
            // a property bag holding a bool for a Number prop (or a non-numeric string)
            // must surface as an error, not a silently-mangled value.
            var guarded = Expression.Call(
                GuardCompatibleInfo,
                Expression.Convert(dictValue, typeof(object)),
                Expression.Constant(targetType.GetClrPrimitive()!.Value));
            return Expression.Convert(
                Expression.Call(null, convertMethod, guarded),
                typeof(object));
        }

        return dictValue;
    }

    private static bool IsIntegralNumber(object raw) => raw switch {
        double d => d == Math.Truncate(d) && !double.IsNaN(d) && !double.IsInfinity(d),
        float f => f == Math.Truncate(f) && !float.IsNaN(f) && !float.IsInfinity(f),
        decimal m => m == decimal.Truncate(m),
        _ => true
    };

    /// <summary>
    /// Validates a raw bag value against the target primitive before coercion. Rejects
    /// values that Convert.* would silently mangle (bool/object → number, null → default),
    /// so the runtime fails loud instead of storing a corrupted value.
    /// </summary>
    internal static object GuardCompatible(object? raw, ClrPrimitive target) {
        if (raw is null) return null!;
        switch (target) {
            case ClrPrimitive.Int64 or ClrPrimitive.Int32 or ClrPrimitive.Int16 or ClrPrimitive.Int8
                or ClrPrimitive.Float64 or ClrPrimitive.Float32 or ClrPrimitive.Decimal:
                if (raw is bool)
                    throw new InvalidOperationException(
                        $"Cannot store a Boolean value in a numeric property (got '{raw}').");
                if (raw is not (long or int or short or byte or sbyte or ushort or uint or ulong or double or float or decimal))
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a numeric property.");
                // Number is long. Convert.ToInt64 would truncate 0.25 to 0.
                if (target == ClrPrimitive.Int64 && raw is double or float or decimal && !IsIntegralNumber(raw))
                    throw new InvalidOperationException(
                        $"Number is integral; '{raw}' is not a valid Number. Use Decimal.");
                return raw;
            case ClrPrimitive.Boolean:
                if (raw is not bool)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a Boolean property.");
                return raw;
            case ClrPrimitive.String:
                if (raw is not string)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a Text property.");
                return raw;
            case ClrPrimitive.Char:
                if (raw is not char and not string)
                    throw new InvalidOperationException(
                        $"Cannot store a value of type '{raw.GetType().Name}' in a character property.");
                return raw;
            default:
                return raw;
        }
    }

    /// <summary>
    /// Runtime counterpart of <see cref="CoerceRead"/>: GuardCompatible, then
    /// the same Convert.* as the member-read expression.
    /// </summary>
    internal static object? CoerceReadValue(object? raw, ClrPrimitive target) {
        var guarded = GuardCompatible(raw, target);
        return target switch {
            ClrPrimitive.Int64 => Convert.ToInt64(guarded),
            ClrPrimitive.Int32 => Convert.ToInt32(guarded),
            ClrPrimitive.Int16 => Convert.ToInt16(guarded),
            ClrPrimitive.Int8 => Convert.ToSByte(guarded),
            ClrPrimitive.Float64 => Convert.ToDouble(guarded),
            ClrPrimitive.Float32 => Convert.ToSingle(guarded),
            ClrPrimitive.Decimal => Convert.ToDecimal(guarded),
            ClrPrimitive.Boolean => Convert.ToBoolean(guarded),
            ClrPrimitive.String => Convert.ToString(guarded),
            ClrPrimitive.Char => Convert.ToChar(guarded),
            _ => guarded
        };
    }
}