using P = Poly.Introspection.PrimitiveType;

namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// What the .NET platform leaves to its compiler: the conversions and operators C# defines for the simple types and
/// string, which the CLR types declare no member for (C# standard v7 §10.2.3, §10.3.2, §10.2.8–9, §12.4.7, §12.9–§12.14),
/// and the native-sized integers <c>nint</c> and <c>nuint</c> as C# 11 defines them (their IntPtr/UIntPtr members stand
/// down: <see cref="IsNativeInteger"/>). The only source of the interface's built-in conversion and operator answers.
/// Decimal's conversions and operators are CLR-declared members and are not repeated here, except a native integer's
/// conversion to and from decimal, which the CLR does not declare. Deliberately none: enum, lifted, reference-equality,
/// delegate and object operators.
/// </summary>
internal static class ClrBuiltInRules {
    /// <summary>
    /// §10.2.3, decimal aside: the implicit numeric conversions from each of the thirteen other numeric types, the
    /// native integers' included (C# 11: §10.2.3 as amended by the native-integer feature). Every other ordered pair of
    /// them is an explicit numeric conversion (§10.3.2).
    /// </summary>
    private static readonly Dictionary<PrimitiveType, PrimitiveType[]> ImplicitNumeric = new() {
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
    /// </summary>
    public static TypeConversion? Conversion(PrimitiveType? from, PrimitiveType? to) {
        if (from == to)
            return null;
        if (from is { } source && to is { } target && ImplicitNumeric.TryGetValue(source, out var implicitTargets) && IsNumeric(target))
            return Intrinsic(implicitTargets.Contains(target) ? ConversionOperatorKind.Implicit : ConversionOperatorKind.Explicit);
        // A native integer to decimal is implicit and back is explicit; the CLR declares neither.
        if (from is P.NInt or P.NUInt && to is P.Decimal)
            return Intrinsic(ConversionOperatorKind.Implicit);
        if (from is P.Decimal && to is P.NInt or P.NUInt)
            return Intrinsic(ConversionOperatorKind.Explicit);
        return to is P.Structure ? Intrinsic(ConversionOperatorKind.Implicit) : null;
    }

    private static TypeConversion Intrinsic(ConversionOperatorKind kind) => new(kind, Method: null);

    private static bool IsNumeric(PrimitiveType type) => ImplicitNumeric.ContainsKey(type);

    private static bool IsIntegral(PrimitiveType type) => type is P.Int32 or P.UInt32 or P.Int64 or P.UInt64 or P.NInt or P.NUInt;

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
    public static PrimitiveType? Promote(PrimitiveType left, PrimitiveType right) {
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
    public static IReadOnlyList<DeclaredOperator> Operators(ClrTypeDefinition owner, PrimitiveType? other, ClrTypeDefinitionRegistry provider, OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(provider);
        if (owner.RuntimeType.GetPrimitiveType() is not { } self)
            return [];
        return [.. Signatures(self, other, kind).Select(signature => new DeclaredOperator(
            new ClrIntrinsicOperator(kind, Definition(signature.Declaring), Definition(signature.Result), [.. signature.Parameters.Select(Definition)]),
            PlatformApplied: true))];

        ClrTypeDefinition Definition(PrimitiveType type) => provider.GetTypeDefinition(type.GetClrType()!);
    }

    private static IEnumerable<(PrimitiveType Declaring, PrimitiveType[] Parameters, PrimitiveType Result)> Signatures(PrimitiveType self, PrimitiveType? other, OperatorKind kind) {
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
                // §12.11: the count converts implicitly to int; GetOperator checks it like any operand.
                if (IsIntegral(own))
                    yield return (own, [own, P.Int32], own);
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
}