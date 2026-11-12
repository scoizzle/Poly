namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// What the .NET platform leaves to its compiler: the conversions and operators C# defines for the primitives,
/// which the CLR types declare no member for. The only source of the interface's built-in conversion and
/// operator answers. Closed to {Int32, Int64, Float64} for numbers, plus Boolean and String: an answer outside
/// the set is none, not unknown (Float32, the unsigned and small integers).
/// </summary>
internal static class ClrBuiltInRules {
    /// <summary>
    /// The built-in conversion between two primitives. Int64 to Float64 is implicit in C#, lossy above 2^53.
    /// </summary>
    public static TypeConversion? Conversion(PrimitiveType? from, PrimitiveType? to) => (from, to) switch {
        (PrimitiveType.Int32, PrimitiveType.Int64) => Intrinsic(ConversionOperatorKind.Implicit),
        (PrimitiveType.Int32, PrimitiveType.Float64) => Intrinsic(ConversionOperatorKind.Implicit),
        (PrimitiveType.Int64, PrimitiveType.Float64) => Intrinsic(ConversionOperatorKind.Implicit),
        (PrimitiveType.Int64, PrimitiveType.Int32) => Intrinsic(ConversionOperatorKind.Explicit),
        (PrimitiveType.Float64, PrimitiveType.Int32) => Intrinsic(ConversionOperatorKind.Explicit),
        (PrimitiveType.Float64, PrimitiveType.Int64) => Intrinsic(ConversionOperatorKind.Explicit),
        _ => null
    };

    private static TypeConversion Intrinsic(ConversionOperatorKind kind) => new(kind, Method: null);

    /// <summary>The operators C# applies itself to <paramref name="owner"/>, resolved through <paramref name="provider"/>.</summary>
    public static IReadOnlyList<DeclaredOperator> Operators(ClrTypeDefinition owner, ClrTypeDefinitionRegistry provider, OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(provider);
        if (ResultOf(owner.RuntimeType, kind) is not { } resultType)
            return [];
        var result = resultType == owner.RuntimeType ? owner : provider.GetTypeDefinition(resultType);
        return [new DeclaredOperator(new ClrIntrinsicOperator(kind, owner, result), PlatformApplied: true)];
    }

    private static Type? ResultOf(Type operand, OperatorKind kind) => (operand.GetPrimitiveType(), kind) switch {
        (PrimitiveType.Int32 or PrimitiveType.Int64 or PrimitiveType.Float64,
            OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide) => operand,
        (PrimitiveType.Int32 or PrimitiveType.Int64 or PrimitiveType.Float64,
            OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
                or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual) => typeof(bool),
        (PrimitiveType.Boolean,
            OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.And or OperatorKind.Or or OperatorKind.Not) => typeof(bool),
        (PrimitiveType.String, OperatorKind.Add) => operand,
        _ => null
    };
}