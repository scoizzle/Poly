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
        Ref.Method((Expression<Func<object?, Type, object>>)((raw, t) => GuardCompatible(raw, t)));

    /// <summary>
    /// Value used when a dictionary key is absent and the member has no constant
    /// default, for every modeled read: an entity member and a value-payload member
    /// alike. A reference-typed target reads <c>null</c>, the absence a required guard
    /// and a <c>null</c> test can see. A converted value type uses that conversion's
    /// zero. <see cref="System.Reflection.Missing"/> cannot be the other branch of a
    /// value-type read: unboxing the condition then casts the zero to <c>Missing</c>.
    /// </summary>
    internal static Expression MissingKeyFallback(ITypeDefinition targetType) {
        var runtime = RuntimeTypeOf(targetType);
        // Text, Binary, and every modeled enum or value record are reference types at
        // runtime, and an unset T? is null too: an absent key is the absence of a value
        // (the owner ruling that null is absence), so the guard sees it instead of
        // Missing, which the required guard would pass and a nested read would cast.
        if (runtime is null || !runtime.IsValueType || Nullable.GetUnderlyingType(runtime) is not null)
            return Expression.Constant(null, typeof(object));
        var convert = ConvertMethod(runtime);
        if (convert is null || !convert.ReturnType.IsValueType)
            return Expression.Convert(Expression.Constant(System.Reflection.Missing.Value), typeof(object));
        var zero = Activator.CreateInstance(convert.ReturnType)!;
        return Expression.Convert(Expression.Constant(zero), typeof(object));
    }

    /// <summary>The runtime type the CLR definition names; a modeled or foreign definition names none.</summary>
    private static Type? RuntimeTypeOf(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr ? clr.RuntimeType : null;

    private static MethodInfo? ConvertMethod(Type? target) => target switch {
        Type t when t == typeof(long) => ConvertToInt64,
        Type t when t == typeof(int) => ConvertToInt32,
        Type t when t == typeof(short) => ConvertToInt16,
        Type t when t == typeof(sbyte) => ConvertToSByte,
        Type t when t == typeof(double) => ConvertToDouble,
        Type t when t == typeof(float) => ConvertToSingle,
        Type t when t == typeof(decimal) => ConvertToDecimal,
        Type t when t == typeof(bool) => ConvertToBoolean,
        Type t when t == typeof(string) => ConvertToString,
        Type t when t == typeof(char) => ConvertToChar,
        _ => null
    };

    internal static Expression CoerceRead(Expression dictValue, ITypeDefinition targetType) {
        var target = RuntimeTypeOf(targetType);
        var convertMethod = ConvertMethod(target);
        if (convertMethod is not null) {
            // Fail loud on fundamentally wrong-typed raw values instead of silently
            // coercing (Convert.ToInt64(true) → 1, Convert.ToString(null) → "", etc.):
            // a property bag holding a bool for a Number prop (or a non-numeric string)
            // must surface as an error, not a silently-mangled value.
            var guarded = Expression.Call(
                GuardCompatibleInfo,
                Expression.Convert(dictValue, typeof(object)),
                Expression.Constant(target, typeof(Type)));
            var converted = Expression.Convert(
                Expression.Call(null, convertMethod, guarded),
                typeof(object));
            // Null is the absence of a value, and an empty string is a value: a Text read
            // of a null bag value stays null instead of becoming "" (owner ruling 2026-11-13).
            if (target != typeof(string))
                return converted;
            return Expression.Condition(
                Expression.Equal(
                    Expression.Convert(dictValue, typeof(object)),
                    Expression.Constant(null, typeof(object))),
                Expression.Constant(null, typeof(object)),
                converted);
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
    internal static object GuardCompatible(object? raw, Type target) {
        if (raw is null) return null!;
        if (target == typeof(long) || target == typeof(int) || target == typeof(short) || target == typeof(sbyte)
            || target == typeof(double) || target == typeof(float) || target == typeof(decimal)) {
            if (raw is bool)
                throw new InvalidOperationException(
                    $"Cannot store a Boolean value in a numeric property (got '{raw}').");
            if (raw is not (long or int or short or byte or sbyte or ushort or uint or ulong or double or float or decimal))
                throw new InvalidOperationException(
                    $"Cannot store a value of type '{raw.GetType().Name}' in a numeric property.");
            // Number is long. Convert.ToInt64 would truncate 0.25 to 0.
            if (target == typeof(long) && raw is double or float or decimal && !IsIntegralNumber(raw))
                throw new InvalidOperationException(
                    $"Number is integral; '{raw}' is not a valid Number. Use Decimal.");
            return raw;
        }
        if (target == typeof(bool)) {
            if (raw is not bool)
                throw new InvalidOperationException(
                    $"Cannot store a value of type '{raw.GetType().Name}' in a Boolean property.");
            return raw;
        }
        if (target == typeof(string)) {
            if (raw is not string)
                throw new InvalidOperationException(
                    $"Cannot store a value of type '{raw.GetType().Name}' in a Text property.");
            return raw;
        }
        if (target == typeof(char)) {
            if (raw is not char and not string)
                throw new InvalidOperationException(
                    $"Cannot store a value of type '{raw.GetType().Name}' in a character property.");
            return raw;
        }
        return raw;
    }

    /// <summary>
    /// Runtime counterpart of <see cref="CoerceRead"/>: GuardCompatible, then
    /// the same Convert.* as the member-read expression.
    /// </summary>
    internal static object? CoerceReadValue(object? raw, Type target) {
        // Null is the absence of a value, and an empty string is a value (owner ruling
        // 2026-11-13): Convert.ToString(null) would turn the absence into a value.
        if (raw is null && target == typeof(string))
            return null;
        var guarded = GuardCompatible(raw, target);
        return target == typeof(long) ? Convert.ToInt64(guarded)
            : target == typeof(int) ? Convert.ToInt32(guarded)
            : target == typeof(short) ? Convert.ToInt16(guarded)
            : target == typeof(sbyte) ? Convert.ToSByte(guarded)
            : target == typeof(double) ? Convert.ToDouble(guarded)
            : target == typeof(float) ? Convert.ToSingle(guarded)
            : target == typeof(decimal) ? Convert.ToDecimal(guarded)
            : target == typeof(bool) ? Convert.ToBoolean(guarded)
            : target == typeof(string) ? Convert.ToString(guarded)
            : target == typeof(char) ? Convert.ToChar(guarded)
            : guarded;
    }
}