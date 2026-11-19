using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using P = Poly.Introspection.CommonLanguageRuntime.ClrPrimitive;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// What the .NET platform leaves to its compiler: the conversions and operators C# defines for the simple types and
/// string, which the CLR types declare no member for (C# standard v7 §10.2.3, §10.3.2, §10.2.8–9, §12.4.7, §12.9–§12.14),
/// and the native-sized integers <c>nint</c> and <c>nuint</c> as C# 11 defines them (their IntPtr/UIntPtr members stand
/// down: <see cref="IsNativeInteger"/>), and C#'s reference and delegate equality (<see cref="ReferenceEquality"/>). The only source of
/// the interface's built-in conversion and operator answers. Decimal's conversions and operators are CLR-declared members
/// and are not repeated here, except a native integer's conversion to and from decimal, which the CLR does not declare.
/// Deliberately none: enum and lifted operators. One named, permanent Poly policy widens C# here (C# rejects,
/// Poly defines), beside Text + X concatenation: <see cref="IsPolicyShiftCount"/>.
/// </summary>
internal static class ClrBuiltInRules {
    /// <summary>
    /// §10.2.3, decimal aside: the implicit numeric conversions from each of the thirteen other numeric types, the
    /// native integers' included (C# 11: §10.2.3 as amended by the native-integer feature). Every other ordered pair of
    /// them is an explicit numeric conversion (§10.3.2).
    /// </summary>
    private static readonly Dictionary<ClrPrimitive, ClrPrimitive[]> ImplicitNumeric = new() {
        [P.Int8] = [P.Int16, P.Int32, P.Int64, P.NInt, P.Float32, P.Float64],
        [P.UInt8] = [P.Int16, P.UInt16, P.Int32, P.UInt32, P.Int64, P.UInt64, P.NInt, P.NUInt, P.Float32, P.Float64],
        [P.Int16] = [P.Int32, P.Int64, P.NInt, P.Float32, P.Float64],
        [P.UInt16] = [P.Int32, P.UInt32, P.Int64, P.UInt64, P.NInt, P.NUInt, P.Float32, P.Float64],
        [P.Int32] = [P.Int64, P.NInt, P.Float32, P.Float64],
        [P.UInt32] = [P.Int64, P.UInt64, P.NUInt, P.Float32, P.Float64],
        [P.Int64] = [P.Float32, P.Float64],
        [P.UInt64] = [P.Float32, P.Float64],
        [P.Char] = [P.UInt16, P.Int32, P.UInt32, P.Int64, P.UInt64, P.NInt, P.NUInt, P.Float32, P.Float64],
        [P.Float32] = [P.Float64],
        [P.Float64] = [],
        [P.NInt] = [P.Int64, P.Float32, P.Float64],
        [P.NUInt] = [P.UInt64, P.Float32, P.Float64]
    };

    /// <summary>
    /// <c>nint</c> or <c>nuint</c>. C# 11 gives them the operators and conversions of a built-in numeric type and ignores
    /// IntPtr's and UIntPtr's own <c>op_*</c> members, so the provider's definition does too: their reflected operators
    /// stand down, and an implicit built-in conversion answers before a declared one.
    /// </summary>
    public static bool IsNativeInteger(Type type) => type == typeof(nint) || type == typeof(nuint);

    /// <summary>
    /// The built-in conversion between two CLR primitives (null: not a primitive). Identity is not a conversion.
    /// Any CLR value converts implicitly to object (§10.2.8 reference, §10.2.9 boxing). Int64 to Float64 is implicit, lossy above 2^53.
    /// Only the kind: the platform applies it, so it names no method, and the caller knows the destination type.
    /// </summary>
    public static ConversionOperatorKind? Conversion(ClrPrimitive? from, ClrPrimitive? to) {
        if (from == to)
            return null;
        if (from is { } source && to is { } target && ImplicitNumeric.TryGetValue(source, out var implicitTargets) && IsNumeric(target))
            return implicitTargets.Contains(target) ? ConversionOperatorKind.Implicit : ConversionOperatorKind.Explicit;
        // A native integer to decimal is implicit and back is explicit; the CLR declares neither.
        if (from is P.NInt or P.NUInt && to is P.Decimal)
            return ConversionOperatorKind.Implicit;
        if (from is P.Decimal && to is P.NInt or P.NUInt)
            return ConversionOperatorKind.Explicit;
        return to is P.Structure ? ConversionOperatorKind.Implicit : null;
    }

    private static bool IsNumeric(ClrPrimitive type) => ImplicitNumeric.ContainsKey(type);

    private static bool IsIntegral(ClrPrimitive type) => type is P.Int32 or P.UInt32 or P.Int64 or P.UInt64 or P.NInt or P.NUInt;

    /// <summary>
    /// The conversion from <paramref name="source"/> to <paramref name="destination"/> when a native integer
    /// (<c>nint</c> or <c>nuint</c>) meets another numeric primitive, or null when neither side is a native integer, a
    /// side is not numeric, or the pair has no conversion. C# 11 applies these conversions itself, so a consumer
    /// converts through the platform and never through a method the platform type declares beside them (IntPtr's
    /// <c>op_Explicit</c> to <c>int</c> is checked; C#'s native conversion is not). A C# rule, so it lives here.
    /// </summary>
    /// <param name="system">The type system that decides whether the conversion exists (#998 T4b).</param>
    public static ConversionOperatorKind? NativeIntegerConversionFrom(ITypeDefinition destination, ITypeDefinition source, ITypeSystem system) {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        if (destination.GetClrPrimitive() is not { } to || source.GetClrPrimitive() is not { } from
            || !(IsNativeInteger(to) || IsNativeInteger(from)) || !IsNumeric(to) || !IsNumeric(from))
            return null;
        return TypeDefinitionExtensions.ConversionInto(destination, source, system)?.Kind;

        static bool IsNativeInteger(ClrPrimitive type) => type is ClrPrimitive.NInt or ClrPrimitive.NUInt;

        static bool IsNumeric(ClrPrimitive type) => type.IsInteger() || IsNativeInteger(type)
            || type is ClrPrimitive.Char or ClrPrimitive.Float32 or ClrPrimitive.Float64 or ClrPrimitive.Decimal;
    }

    /// <summary>
    /// Poly policy, permanent (owner ruling, #677, 2026-11-13; C# rejects, Poly defines): a shift count may be any built-in
    /// integer type. C# (§12.11) defines a shift only for a count that converts implicitly to <c>int</c>; this names the
    /// others, <c>long</c>, <c>uint</c>, <c>ulong</c>, <c>nint</c> and <c>nuint</c>. Their operator takes the count as its
    /// own type and its result is the left operand's as for an <c>int</c> count. The count is converted to <c>int</c>
    /// unchecked, then masked by the shift: 5 bits for a 32-bit left operand, 6 for a 64-bit one. The lowered tree states the
    /// conversion as a cast.
    /// </summary>
    /// <summary>The CLR's own calendar types (DateTime, DateOnly, TimeOnly, DateTimeOffset, TimeSpan): the compatibility
    /// analyzer's Date category, which matches the CLR's definitions (owner ruling 2026-11-19; its removing row is I8b,
    /// #972). Only the CLR's own definitions answer.</summary>
    public static bool IsCalendarType(ITypeDefinition definition) =>
        definition.GetClrPrimitive() is P.DateTime or P.DateOnly or P.TimeOnly or P.DateTimeOffset or P.TimeSpan;

    /// <summary>A timestamp: exactly the CLR's DateTime or DateTimeOffset.</summary>
    public static bool IsTimestamp(ITypeDefinition definition) =>
        definition.GetClrPrimitive() is P.DateTime or P.DateTimeOffset;

    /// <summary>The CLR's Guid: the compatibility analyzer's Guid category (same ruling and removing row).</summary>
    public static bool IsGuid(ITypeDefinition definition) => definition.GetClrPrimitive() is P.Guid;

    /// <summary>One of the CLR host's own primitive types (its table: bool through byte[], and object), by identity.</summary>
    public static bool IsPrimitive(ITypeDefinition definition) => definition.GetClrPrimitive() is not null;

    /// <summary>The CLR's byte[] as a value: the product pack's scalar (same ruling).</summary>
    public static bool IsByteArray(ITypeDefinition definition) => definition.GetClrPrimitive() is P.ByteArray;

    public static bool IsPolicyShiftCount(ClrPrimitive type) => type is P.Int64 or P.UInt32 or P.UInt64 or P.NInt or P.NUInt;

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
    /// Numeric promotion over the thirteen non-decimal numeric types: the type both operands convert to, or null when
    /// C# has no predefined operator for the pair. §12.4.7.3 for a pair; a type with itself is §12.4.7.2's unary
    /// promotion (sbyte, byte, short, ushort and char to int; nint and nuint stay themselves). Decimal is absent: its
    /// operators are the CLR's own, so §12.4.7.3's decimal-with-float error falls out of decimal's declared members.
    /// The native integers (C# 11) answer before Int64: <c>nint</c> with <c>uint</c> or <c>long</c> is <c>long</c>,
    /// with a smaller type or <c>char</c> it is <c>nint</c>; <c>nuint</c> with an unsigned type smaller than
    /// <c>ulong</c> or <c>char</c> is <c>nuint</c>; <c>nuint</c> with a signed type, and <c>nint</c> with <c>nuint</c>, has
    /// no operator.
    /// </summary>
    public static ClrPrimitive? Promote(ClrPrimitive left, ClrPrimitive right) {
        if (left is P.Float64 || right is P.Float64)
            return P.Float64;
        if (left is P.Float32 || right is P.Float32)
            return P.Float32;
        if (left is P.UInt64 || right is P.UInt64)
            return left is P.Int8 or P.Int16 or P.Int32 or P.Int64 or P.NInt || right is P.Int8 or P.Int16 or P.Int32 or P.Int64 or P.NInt ? null : P.UInt64;
        if (left is P.NInt or P.NUInt || right is P.NInt or P.NUInt) {
            if (left == right)
                return left;
            if (left is P.NUInt || right is P.NUInt)
                return (left is P.NUInt ? right : left) is P.UInt8 or P.UInt16 or P.UInt32 or P.Char ? P.NUInt : null;
            return (left is P.NInt ? right : left) is P.UInt32 or P.Int64 ? P.Int64 : P.NInt;
        }
        if (left is P.Int64 || right is P.Int64)
            return P.Int64;
        if (left is P.UInt32 || right is P.UInt32)
            return left is P.Int8 or P.Int16 or P.Int32 || right is P.Int8 or P.Int16 or P.Int32 ? P.Int64 : P.UInt32;
        return P.Int32;
    }

    /// <summary>
    /// The operators C# applies itself when <paramref name="owner"/> meets an operand of primitive <paramref name="other"/>
    /// (null: not a primitive). Each is declared by the type it operates on (the promoted type, Boolean or String),
    /// so both operands of a pair reach the same operator. The definition does not know its side: a shift answers as
    /// the left operand, and a right operand answering as left yields (Int32, Int32) or nothing that applies.
    /// </summary>
    public static IReadOnlyList<ClrTypeOperator> Operators(ClrTypeDefinition owner, ClrPrimitive? other, ClrTypeDefinitionRegistry provider, OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(provider);
        if (owner.RuntimeType.GetPrimitiveType() is not { } self)
            return [];
        return [.. Signatures(self, other, kind).Select(signature =>
            new ClrTypeOperator(kind, Definition(signature.Declaring), Definition(signature.Result), [.. signature.Parameters.Select(Definition)]))];

        ClrTypeDefinition Definition(ClrPrimitive type) => provider.GetTypeDefinition(type.GetClrType()!);
    }

    private static IEnumerable<(ClrPrimitive Declaring, ClrPrimitive[] Parameters, ClrPrimitive Result)> Signatures(ClrPrimitive self, ClrPrimitive? other, OperatorKind kind) {
        if (self is P.Boolean) {
            if (kind is OperatorKind.Not)
                yield return (P.Boolean, [P.Boolean], P.Boolean);
            else if (kind is OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.And or OperatorKind.Or
                or OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor)
                yield return (P.Boolean, [P.Boolean, P.Boolean], P.Boolean);
            yield break;
        }
        if (self is P.String) {
            // §12.10.5: string + string, string + object and object + string.
            if (kind is not OperatorKind.Add)
                yield break;
            yield return (P.String, [P.String, P.String], P.String);
            if (other is not P.String) {
                yield return (P.String, [P.String, P.Structure], P.String);
                yield return (P.String, [P.Structure, P.String], P.String);
            }
            yield break;
        }
        if (!IsNumeric(self))
            yield break;

        var own = Promote(self, self)!.Value;
        // Beside a non-numeric operand the owner offers its own operator, which a conversion of that operand may reach.
        var pair = other is { } o && IsNumeric(o) ? Promote(self, o) : own;
        switch (kind) {
            case OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo:
                if (pair is { } arithmetic)
                    yield return (arithmetic, [arithmetic, arithmetic], arithmetic);
                break;
            case OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual:
                if (pair is { } compared)
                    yield return (compared, [compared, compared], P.Boolean);
                break;
            case OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor:
                if (pair is { } bitwise && IsIntegral(bitwise))
                    yield return (bitwise, [bitwise, bitwise], bitwise);
                break;
            case OperatorKind.ShiftLeft or OperatorKind.ShiftRight:
                // §12.11: the count converts implicitly to int; GetOperator checks it like any operand. A wider integer
                // count is Poly's policy (IsPolicyShiftCount): the operator takes it as it is.
                if (IsIntegral(own)) {
                    yield return (own, [own, P.Int32], own);
                    if (other is { } count && IsPolicyShiftCount(count))
                        yield return (own, [own, count], own);
                }
                break;
            case OperatorKind.UnaryPlus:
                yield return (own, [own], own);
                break;
            case OperatorKind.UnaryMinus:
                // §12.9.3: -uint is long; -ulong and -nuint have no operator.
                if (own is P.UInt32)
                    yield return (P.Int64, [P.Int64], P.Int64);
                else if (own is not (P.UInt64 or P.NUInt))
                    yield return (own, [own], own);
                break;
            case OperatorKind.BitwiseNot:
                if (IsIntegral(own))
                    yield return (own, [own], own);
                break;
        }
    }

    // ── What C#'s type system offers for the CLR's definitions (#998 T10a). The definitions list only what they
    // declare; C#'s system asks these. Interim, removed by T10b: the tables are keyed on the CLR host's ClrPrimitive
    // and live here, not on C#'s SimpleType in Interpretation/CSharp. ──

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
            builtInKind = Conversion(ClrTypeDefinition.GetPrimitiveTypeId(clrSource.RuntimeType), ClrTypeDefinition.GetPrimitiveTypeId(destination.RuntimeType));
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

    private static readonly ConditionalWeakTable<ClrTypeDefinition, ConcurrentDictionary<(OperatorKind Kind, ClrPrimitive? Other), IReadOnlyList<ITypeOperator>>> Beside = new();

    /// <summary>
    /// The operators C# offers for <paramref name="owner"/> beside an operand of <paramref name="other"/> (null: the
    /// owner itself): the operators C# applies itself, then the owner's declared <c>op_*</c> members. Where the CLR also
    /// declares an intrinsic's exact signature (Double and Single declare their comparisons), the intrinsic is the
    /// answer. The built-in rules read the other operand only through its primitive, so the cache is keyed by the kind
    /// and that primitive: keyed by the kind alone, the first pair would answer for every other; keyed by the other
    /// definition, it would grow without bound on fresh synthetic collection types, which are a new instance per
    /// resolve. Reference equality depends on the other definition itself, so it is answered beside the cache.
    /// </summary>
    public static IReadOnlyList<ITypeOperator> OperatorsBeside(ClrTypeDefinition owner, OperatorKind kind, ITypeDefinition? other) {
        ArgumentNullException.ThrowIfNull(owner);
        OperatorShape.ArityOf(kind);
        var otherPrimitive = other is null ? ClrTypeDefinition.GetPrimitiveTypeId(owner.RuntimeType) : other.GetClrPrimitive();
        var offered = Beside.GetValue(owner, static _ => new()).GetOrAdd((kind, otherPrimitive), key => {
            var intrinsic = Operators(owner, key.Other, owner.Provider, key.Kind);
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