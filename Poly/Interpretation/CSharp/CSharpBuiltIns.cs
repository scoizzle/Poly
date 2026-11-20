using System.Collections.Concurrent;

using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// What the .NET platform leaves to its compiler: the conversions and operators C# defines for its simple types and
/// string, which the CLR types declare no member for (C# standard v7 §10.2.3, §10.3.2, §10.2.8–9, §12.4.7, §12.9–§12.14),
/// and the native-sized integers <c>nint</c> and <c>nuint</c> as C# 11 defines them (their IntPtr/UIntPtr members stand
/// down: <see cref="IsNativeInteger(Type)"/>), and C#'s reference and delegate equality (<see cref="ReferenceEquality"/>).
/// The only source of the interface's built-in conversion and operator answers. Decimal's conversions and operators are
/// CLR-declared members and are not repeated here, except a native integer's conversion to and from decimal, which the
/// CLR does not declare. Deliberately none: enum and lifted operators. One named, permanent Poly policy widens C# here
/// (C# rejects, Poly defines), beside Text + X concatenation: <see cref="IsPolicyShiftCount"/>. The rules key on C#'s
/// <see cref="SimpleType"/> and on runtime-type identity for string and object, the two other types C# treats as built
/// in (#998 T10b: the CLR host's own primitive enum is gone).
/// </summary>
internal static class CSharpBuiltIns {
    /// <summary>
    /// §10.2.3, decimal aside: the implicit numeric conversions from each of the thirteen other numeric types, the
    /// native integers' included (C# 11: §10.2.3 as amended by the native-integer feature). Every other ordered pair of
    /// them is an explicit numeric conversion (§10.3.2).
    /// </summary>
    private static readonly Dictionary<SimpleType, SimpleType[]> ImplicitNumeric = new() {
        [SimpleType.SByte] = [SimpleType.Short, SimpleType.Int, SimpleType.Long, SimpleType.NInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.Byte] = [SimpleType.Short, SimpleType.UShort, SimpleType.Int, SimpleType.UInt, SimpleType.Long, SimpleType.ULong, SimpleType.NInt, SimpleType.NUInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.Short] = [SimpleType.Int, SimpleType.Long, SimpleType.NInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.UShort] = [SimpleType.Int, SimpleType.UInt, SimpleType.Long, SimpleType.ULong, SimpleType.NInt, SimpleType.NUInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.Int] = [SimpleType.Long, SimpleType.NInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.UInt] = [SimpleType.Long, SimpleType.ULong, SimpleType.NUInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.Long] = [SimpleType.Float, SimpleType.Double],
        [SimpleType.ULong] = [SimpleType.Float, SimpleType.Double],
        [SimpleType.Char] = [SimpleType.UShort, SimpleType.Int, SimpleType.UInt, SimpleType.Long, SimpleType.ULong, SimpleType.NInt, SimpleType.NUInt, SimpleType.Float, SimpleType.Double],
        [SimpleType.Float] = [SimpleType.Double],
        [SimpleType.Double] = [],
        [SimpleType.NInt] = [SimpleType.Long, SimpleType.Float, SimpleType.Double],
        [SimpleType.NUInt] = [SimpleType.ULong, SimpleType.Float, SimpleType.Double]
    };

    /// <summary>The CLR type a C# simple type denotes.</summary>
    private static Type RuntimeTypeOf(SimpleType type) => type switch {
        SimpleType.Bool => typeof(bool),
        SimpleType.Char => typeof(char),
        SimpleType.SByte => typeof(sbyte),
        SimpleType.Byte => typeof(byte),
        SimpleType.Short => typeof(short),
        SimpleType.UShort => typeof(ushort),
        SimpleType.Int => typeof(int),
        SimpleType.UInt => typeof(uint),
        SimpleType.Long => typeof(long),
        SimpleType.ULong => typeof(ulong),
        SimpleType.NInt => typeof(nint),
        SimpleType.NUInt => typeof(nuint),
        SimpleType.Float => typeof(float),
        SimpleType.Double => typeof(double),
        SimpleType.Decimal => typeof(decimal),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// <c>nint</c> or <c>nuint</c>. C# 11 gives them the operators and conversions of a built-in numeric type and ignores
    /// IntPtr's and UIntPtr's own <c>op_*</c> members, so the provider's definition does too: their reflected operators
    /// stand down, and an implicit built-in conversion answers before a declared one.
    /// </summary>
    public static bool IsNativeInteger(Type type) => type == typeof(nint) || type == typeof(nuint);

    private static bool IsNativeInteger(SimpleType? type) => type is SimpleType.NInt or SimpleType.NUInt;

    /// <summary>
    /// Poly policy, permanent (owner ruling, #677, 2026-11-13; C# rejects, Poly defines): a shift count may be any built-in
    /// integer type. C# (§12.11) defines a shift only for a count that converts implicitly to <c>int</c>; this names the
    /// others, <c>long</c>, <c>uint</c>, <c>ulong</c>, <c>nint</c> and <c>nuint</c>. Their operator takes the count as its
    /// own type and its result is the left operand's as for an <c>int</c> count. The count is converted to <c>int</c>
    /// unchecked, then masked by the shift: 5 bits for a 32-bit left operand, 6 for a 64-bit one. The lowered tree states the
    /// conversion as a cast.
    /// </summary>
    public static bool IsPolicyShiftCount(SimpleType? type) =>
        type is SimpleType.Long or SimpleType.UInt or SimpleType.ULong or SimpleType.NInt or SimpleType.NUInt;

    /// <summary>
    /// The built-in conversion between two CLR types that C# names a built-in for (null: not one). Identity is not a
    /// conversion. Any CLR value converts implicitly to object (§10.2.8 reference, §10.2.9 boxing). Int64 to Float64 is
    /// implicit, lossy above 2^53. Only the kind: the platform applies it, so it names no method, and the caller knows
    /// the destination type.
    /// </summary>
    public static ConversionOperatorKind? Conversion(Type? from, Type? to) {
        if (from == to)
            return null;
        var source = CSharpTypeSystem.SimpleTypeOf(from);
        var target = CSharpTypeSystem.SimpleTypeOf(to);
        if (source is { } sourceSimple && target is { } targetSimple
            && ImplicitNumeric.TryGetValue(sourceSimple, out var implicitTargets) && IsNumeric(targetSimple))
            return implicitTargets.Contains(targetSimple) ? ConversionOperatorKind.Implicit : ConversionOperatorKind.Explicit;
        // A native integer to decimal is implicit and back is explicit; the CLR declares neither.
        if (IsNativeInteger(source) && target is SimpleType.Decimal)
            return ConversionOperatorKind.Implicit;
        if (source is SimpleType.Decimal && IsNativeInteger(target))
            return ConversionOperatorKind.Explicit;
        return to == typeof(object) ? ConversionOperatorKind.Implicit : null;
    }

    private static bool IsNumeric(SimpleType type) => ImplicitNumeric.ContainsKey(type);

    private static bool IsIntegral(SimpleType type) =>
        type is SimpleType.Int or SimpleType.UInt or SimpleType.Long or SimpleType.ULong or SimpleType.NInt or SimpleType.NUInt;

    /// <summary>
    /// The conversion from <paramref name="source"/> to <paramref name="destination"/> when a native integer
    /// (<c>nint</c> or <c>nuint</c>) meets another numeric primitive, or null when neither side is a native integer, a
    /// side is not numeric, or the pair has no conversion. C# 11 applies these conversions itself, so a consumer
    /// converts through the platform and never through a method the platform type declares beside them (IntPtr's
    /// <c>op_Explicit</c> to <c>int</c> is checked; C#'s native conversion is not).
    /// </summary>
    /// <param name="system">The type system that decides whether the conversion exists (#998 T4b).</param>
    public static ConversionOperatorKind? NativeIntegerConversionFrom(ITypeDefinition destination, ITypeDefinition source, ITypeSystem system) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        var to = (destination as ClrTypeDefinition)?.RuntimeType;
        var from = (source as ClrTypeDefinition)?.RuntimeType;
        if (to is null || from is null
            || !(IsNativeInteger(to) || IsNativeInteger(from)) || !IsNumeric(to) || !IsNumeric(from))
            return null;
        return TypeDefinitionExtensions.ConversionInto(destination, source, system)?.Kind;

        static bool IsNumeric(Type type) => CSharpTypeSystem.SimpleTypeOf(type) is { } simple
            && (ImplicitNumeric.ContainsKey(simple) || simple is SimpleType.Decimal);
    }

    /// <summary>
    /// Numeric promotion over the thirteen non-decimal numeric types: the type both operands convert to, or null when
    /// C# has no predefined operator for the pair. §12.4.7.3 for a pair; a type with itself is §12.4.7.2's unary
    /// promotion (sbyte, byte, short, ushort and char to int; nint and nuint stay themselves). Decimal is absent: its
    /// operators are the CLR's own, so §12.4.7.3's decimal-with-float error falls out of decimal's declared members.
    /// The native integers (C# 11) answer before Int64: <c>nint</c> with <c>uint</c> or <c>long</c> is <c>long</c>,
    /// with a smaller type or <c>char</c> it is <c>nint</c>; <c>nuint</c> with an unsigned type smaller than
    /// <c>ulong</c> or <c>char</c> is <c>nuint</c>; <c>nuint</c> with a signed type, and <c>nint</c> with <c>nuint</c>, has
    /// no operator.
    /// </summary>
    public static SimpleType? Promote(SimpleType left, SimpleType right) {
        if (left is SimpleType.Double || right is SimpleType.Double)
            return SimpleType.Double;
        if (left is SimpleType.Float || right is SimpleType.Float)
            return SimpleType.Float;
        if (left is SimpleType.ULong || right is SimpleType.ULong)
            return left is SimpleType.SByte or SimpleType.Short or SimpleType.Int or SimpleType.Long or SimpleType.NInt
                || right is SimpleType.SByte or SimpleType.Short or SimpleType.Int or SimpleType.Long or SimpleType.NInt
                ? null : SimpleType.ULong;
        if (left is SimpleType.NInt or SimpleType.NUInt || right is SimpleType.NInt or SimpleType.NUInt) {
            if (left == right)
                return left;
            if (left is SimpleType.NUInt || right is SimpleType.NUInt)
                return (left is SimpleType.NUInt ? right : left) is SimpleType.Byte or SimpleType.UShort or SimpleType.UInt or SimpleType.Char
                    ? SimpleType.NUInt : null;
            return (left is SimpleType.NInt ? right : left) is SimpleType.UInt or SimpleType.Long ? SimpleType.Long : SimpleType.NInt;
        }
        if (left is SimpleType.Long || right is SimpleType.Long)
            return SimpleType.Long;
        if (left is SimpleType.UInt || right is SimpleType.UInt)
            return left is SimpleType.SByte or SimpleType.Short or SimpleType.Int
                || right is SimpleType.SByte or SimpleType.Short or SimpleType.Int ? SimpleType.Long : SimpleType.UInt;
        return SimpleType.Int;
    }

    /// <summary>
    /// The operators C# applies itself when <paramref name="owner"/> meets an operand of the built-in <paramref name="other"/>
    /// (null: the owner itself). Each is declared by the type it operates on (the promoted type, Boolean or String), so
    /// both operands of a pair reach the same operator. The definition does not know its side: a shift answers as the
    /// left operand, and a right operand answering as left yields (Int32, Int32) or nothing that applies.
    /// </summary>
    private static IReadOnlyList<ClrTypeOperator> Operators(
        ClrTypeDefinition owner, SimpleType? other, bool otherIsString, ClrTypeDefinitionRegistry provider, OperatorKind kind) {
        var self = owner.RuntimeType;
        if (self != typeof(bool) && self != typeof(string) && CSharpTypeSystem.SimpleTypeOf(self) is null)
            return [];
        return [.. Signatures(self, other, otherIsString, kind).Select(signature =>
            new ClrTypeOperator(kind, provider.GetTypeDefinition(signature.Declaring), provider.GetTypeDefinition(signature.Result),
                [.. signature.Parameters.Select(provider.GetTypeDefinition)]))];
    }

    private static IEnumerable<(Type Declaring, Type[] Parameters, Type Result)> Signatures(
        Type self, SimpleType? other, bool otherIsString, OperatorKind kind) {
        if (self == typeof(bool)) {
            if (kind is OperatorKind.Not)
                yield return (typeof(bool), [typeof(bool)], typeof(bool));
            else if (kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.And or OperatorKind.Or
                or OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor)
                yield return (typeof(bool), [typeof(bool), typeof(bool)], typeof(bool));
            yield break;
        }
        if (self == typeof(string)) {
            // §12.10.5: string + string, string + object and object + string.
            if (kind is not OperatorKind.Add)
                yield break;
            yield return (typeof(string), [typeof(string), typeof(string)], typeof(string));
            if (!otherIsString) {
                yield return (typeof(string), [typeof(string), typeof(object)], typeof(string));
                yield return (typeof(string), [typeof(object), typeof(string)], typeof(string));
            }
            yield break;
        }
        if (CSharpTypeSystem.SimpleTypeOf(self) is not { } numeric || !IsNumeric(numeric))
            yield break;

        var own = Promote(numeric, numeric)!.Value;
        // Beside a non-numeric operand the owner offers its own operator, which a conversion of that operand may reach.
        var pair = other is { } o && IsNumeric(o) ? Promote(numeric, o) : own;
        switch (kind) {
            case OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo:
                if (pair is { } arithmetic)
                    yield return (RuntimeTypeOf(arithmetic), [RuntimeTypeOf(arithmetic), RuntimeTypeOf(arithmetic)], RuntimeTypeOf(arithmetic));
                break;
            case OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual:
                if (pair is { } compared)
                    yield return (RuntimeTypeOf(compared), [RuntimeTypeOf(compared), RuntimeTypeOf(compared)], typeof(bool));
                break;
            case OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor:
                if (pair is { } bitwise && IsIntegral(bitwise))
                    yield return (RuntimeTypeOf(bitwise), [RuntimeTypeOf(bitwise), RuntimeTypeOf(bitwise)], RuntimeTypeOf(bitwise));
                break;
            case OperatorKind.ShiftLeft or OperatorKind.ShiftRight:
                // §12.11: the count converts implicitly to int; GetOperator checks it like any operand. A wider integer
                // count is Poly's policy (IsPolicyShiftCount): the operator takes it as it is.
                if (IsIntegral(own)) {
                    yield return (RuntimeTypeOf(own), [RuntimeTypeOf(own), typeof(int)], RuntimeTypeOf(own));
                    if (other is { } count && IsPolicyShiftCount(count))
                        yield return (RuntimeTypeOf(own), [RuntimeTypeOf(own), RuntimeTypeOf(count)], RuntimeTypeOf(own));
                }
                break;
            case OperatorKind.UnaryPlus:
                yield return (RuntimeTypeOf(own), [RuntimeTypeOf(own)], RuntimeTypeOf(own));
                break;
            case OperatorKind.UnaryMinus:
                // §12.9.3: -uint is long; -ulong and -nuint have no operator.
                if (own is SimpleType.UInt)
                    yield return (typeof(long), [typeof(long)], typeof(long));
                else if (own is not (SimpleType.ULong or SimpleType.NUInt))
                    yield return (RuntimeTypeOf(own), [RuntimeTypeOf(own)], RuntimeTypeOf(own));
                break;
            case OperatorKind.BitwiseNot:
                if (IsIntegral(own))
                    yield return (RuntimeTypeOf(own), [RuntimeTypeOf(own)], RuntimeTypeOf(own));
                break;
        }
    }

    /// <summary>
    /// C# 12.12.7, reference type equality: <c>bool ==(object, object)</c> and <c>!=</c>, declared by <c>object</c>, when both
    /// operands are reference types and a reference conversion exists between them in either direction: an identity or
    /// implicit one (a base class, an implemented interface, array covariance, <c>object</c>), or an explicit one through an
    /// interface (an interface beside another interface or beside a class that is not sealed). Two unrelated classes have
    /// none. A value-type operand never has it (C# boxes no value for reference equality); the null literal is the caller's.
    /// Overload resolution prefers a type's own equality: <c>string == string</c> stays <c>String.op_Equality</c>.
    /// Two delegate operands are C# 12.12.9 instead, as Roslyn binds it: one delegate type <c>D</c> on both sides has
    /// <c>bool ==(D, D)</c>, declared by <c>D</c>, which compares invocation lists; two different delegate types have none
    /// (CS0019). A delegate beside <c>object</c> or <c>System.Delegate</c> is reference equality.
    /// </summary>
    public static ClrTypeOperator? ReferenceEquality(ClrTypeDefinition owner, ITypeDefinition? other, ClrTypeDefinitionRegistry provider, OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(provider);
        if (kind is not (OperatorKind.Equal or OperatorKind.NotEqual))
            return null;
        var left = owner.RuntimeType;
        var right = other is null ? left : other is ClrTypeDefinition clr ? clr.RuntimeType : null;
        if (right is null || !IsReference(left) || !IsReference(right))
            return null;
        if (IsDelegateType(left) && IsDelegateType(right)) {
            if (left != right)
                return null;
            return new ClrTypeOperator(kind, owner, provider.GetTypeDefinition(typeof(bool)), [owner, owner]);
        }
        if (!ReferenceConversionExists(left, right))
            return null;
        var objectDefinition = provider.GetTypeDefinition(typeof(object));
        return new ClrTypeOperator(kind, objectDefinition, provider.GetTypeDefinition(typeof(bool)), [objectDefinition, objectDefinition]);
    }

    private static bool IsReference(Type type) =>
        !type.IsValueType && !type.IsPointer && !type.IsByRef && !type.IsGenericParameter;

    /// <summary>A delegate type (<c>Action</c>, <c>Func&lt;T&gt;</c>, …), not <c>System.Delegate</c> or <c>MulticastDelegate</c> themselves.</summary>
    private static bool IsDelegateType(Type type) => type.IsSubclassOf(typeof(MulticastDelegate));

    private static bool ReferenceConversionExists(Type left, Type right) =>
        left.IsAssignableFrom(right) || right.IsAssignableFrom(left)
        || (left.IsInterface && (right.IsInterface || !right.IsSealed))
        || (right.IsInterface && !left.IsSealed);

    /// <summary>
    /// C#'s conversion into <paramref name="destination"/> (§2.2's order, moved from the definition): a native integer's
    /// implicit built-in conversion first (C# 11 makes <c>nint</c> to <c>long</c> implicit where IntPtr declares an
    /// explicit <c>op_Explicit</c>; an explicit one keeps IntPtr's declared method), then the declared conversion
    /// (implicit preferred), so a conversion that names a method is never replaced by a built-in rule; then the built-in
    /// rules, only between two CLR definitions; then C# 10.6.1's implicit nullable conversion.
    /// </summary>
    public static ITypeConversion? ConversionInto(ClrTypeDefinition destination, ITypeDefinition source) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        var clrSource = source as ClrTypeDefinition;
        ConversionOperatorKind? builtInKind = null;
        if (clrSource is not null) {
            builtInKind = Conversion(clrSource.RuntimeType, destination.RuntimeType);
            if (builtInKind is ConversionOperatorKind.Implicit
                && (IsNativeInteger(clrSource.RuntimeType) || IsNativeInteger(destination.RuntimeType)))
                return ClrTypeConversion.Intrinsic(builtInKind.Value, source, destination);
        }
        if (destination.FindDeclaredConversion(source, ConversionOperatorKind.Implicit) is { } declaredImplicit)
            return declaredImplicit;
        if (destination.FindDeclaredConversion(source, ConversionOperatorKind.Explicit) is { } declaredExplicit)
            return declaredExplicit;
        if (builtInKind is { } kind)
            return ClrTypeConversion.Intrinsic(kind, source, destination);
        return clrSource is null ? null : LiftedImplicit(destination, clrSource);
    }

    /// <summary>
    /// C# 10.6.1's implicit nullable conversion: into <c>U?</c> from <c>S</c> or <c>S?</c> where <c>S</c> converts to
    /// <c>U</c> implicitly (<c>long</c> to <c>decimal?</c>, <c>int?</c> to <c>long?</c>). It names no method, whatever the
    /// underlying conversion declares, so it is never assignability; the emitter applies it from the bound parameter type.
    /// Only for a nullable target. <c>S</c> to <c>S?</c> is the lift assignability states.
    /// </summary>
    private static ITypeConversion? LiftedImplicit(ClrTypeDefinition destination, ClrTypeDefinition source) {
        var underlying = Nullable.GetUnderlyingType(destination.RuntimeType);
        if (underlying is null)
            return null;
        var sourceUnderlying = Nullable.GetUnderlyingType(source.RuntimeType) ?? source.RuntimeType;
        if (sourceUnderlying == underlying)
            return null;
        var underlyingConversion = ConversionInto(destination.Provider.GetTypeDefinition(underlying),
            destination.Provider.GetTypeDefinition(sourceUnderlying));
        return underlyingConversion is { Kind: ConversionOperatorKind.Implicit }
            ? ClrTypeConversion.Intrinsic(ConversionOperatorKind.Implicit, source, destination)
            : null;
    }

    private static readonly ConditionalWeakTable<ClrTypeDefinition, ConcurrentDictionary<(OperatorKind Kind, SimpleType? Other, bool OtherIsString), IReadOnlyList<ITypeOperator>>> Beside = new();

    /// <summary>
    /// The operators C# offers for <paramref name="owner"/> beside an operand of <paramref name="other"/> (null: the
    /// owner itself): the operators C# applies itself, then the owner's declared <c>op_*</c> members. Where the CLR also
    /// declares an intrinsic's exact signature (Double and Single declare their comparisons), the intrinsic is the
    /// answer. The built-in rules read the other operand only through its simple type and whether it is string, so the
    /// cache is keyed by the kind and those: keyed by the kind alone, the first pair would answer for every other; keyed
    /// by the other definition, it would grow without bound on fresh synthetic collection types, which are a new
    /// instance per resolve. Reference equality depends on the other definition itself, so it is answered beside the
    /// cache.
    /// </summary>
    public static IReadOnlyList<ITypeOperator> OperatorsBeside(ClrTypeDefinition owner, OperatorKind kind, ITypeDefinition? other) {
        ArgumentNullException.ThrowIfNull(owner);
        OperatorShape.ArityOf(kind);
        var effectiveOther = other ?? owner;
        var otherSimple = CSharpTypeSystem.Shared.SimpleTypeOf(effectiveOther);
        var otherIsString = effectiveOther is ClrTypeDefinition clr && clr.RuntimeType == typeof(string);
        var offered = Beside.GetValue(owner, static _ => new()).GetOrAdd((kind, otherSimple, otherIsString), key => {
            var intrinsic = Operators(owner, key.Other, key.OtherIsString, owner.Provider, key.Kind);
            var declared = owner.Operators.Where(o => o.Kind == key.Kind && !intrinsic.Any(builtIn => SameSignature(builtIn, o)));
            return [.. intrinsic, .. declared];
        });
        return ReferenceEquality(owner, other, owner.Provider, kind) is { } reference ? [.. offered, reference] : offered;
    }

    private static bool SameSignature(ITypeOperator intrinsic, ITypeOperator declared) {
        var (pa, pb) = (intrinsic.Parameters.ToArray(), declared.Parameters.ToArray());
        return ReferenceEquals(intrinsic.ResultTypeDefinition, declared.ResultTypeDefinition)
            && pa.Length == pb.Length
            && pa.Zip(pb).All(static pair => ReferenceEquals(pair.First.ParameterTypeDefinition, pair.Second.ParameterTypeDefinition));
    }
}