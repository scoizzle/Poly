namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's special names (ECMA-335 I.10.3): how C# and the CLR encode an operator or a conversion as a static
/// member. One table because it is one encoding; the two root vocabularies stay apart at the boundary.
/// <see cref="OperatorKind.And"/> and <see cref="OperatorKind.Or"/> have no member form: <c>op_BitwiseAnd</c> and
/// <c>op_BitwiseOr</c> are <see cref="OperatorKind.BitwiseAnd"/> and <see cref="OperatorKind.BitwiseOr"/>.
/// </summary>
internal static class ClrSpecialNames {
    /// <summary>The member name for <paramref name="kind"/>, or null when the kind has no member form.</summary>
    public static string? OperatorName(OperatorKind kind) => kind switch {
        OperatorKind.Add => "op_Addition",
        OperatorKind.Subtract => "op_Subtraction",
        OperatorKind.Multiply => "op_Multiply",
        OperatorKind.Divide => "op_Division",
        OperatorKind.Modulo => "op_Modulus",
        OperatorKind.Equal => "op_Equality",
        OperatorKind.NotEqual => "op_Inequality",
        OperatorKind.LessThan => "op_LessThan",
        OperatorKind.LessThanOrEqual => "op_LessThanOrEqual",
        OperatorKind.GreaterThan => "op_GreaterThan",
        OperatorKind.GreaterThanOrEqual => "op_GreaterThanOrEqual",
        OperatorKind.Not => "op_LogicalNot",
        OperatorKind.UnaryMinus => "op_UnaryNegation",
        OperatorKind.UnaryPlus => "op_UnaryPlus",
        OperatorKind.BitwiseAnd => "op_BitwiseAnd",
        OperatorKind.BitwiseOr => "op_BitwiseOr",
        OperatorKind.BitwiseXor => "op_ExclusiveOr",
        OperatorKind.BitwiseNot => "op_OnesComplement",
        OperatorKind.ShiftLeft => "op_LeftShift",
        OperatorKind.ShiftRight => "op_RightShift",
        OperatorKind.And or OperatorKind.Or => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Operator kind has no name.")
    };

    private static readonly Dictionary<string, OperatorKind> OperatorKindsByName = Enum.GetValues<OperatorKind>()
        .Where(static kind => OperatorName(kind) is not null)
        .ToDictionary(static kind => OperatorName(kind)!, StringComparer.Ordinal);

    /// <summary>The operator a member named <paramref name="name"/> encodes, or null when the name is no operator's.</summary>
    public static OperatorKind? OperatorKindOf(string name) =>
        OperatorKindsByName.TryGetValue(name, out var kind) ? kind : null;

    /// <summary>The member name for <paramref name="kind"/>.</summary>
    public static string ConversionName(ConversionOperatorKind kind) => kind switch {
        ConversionOperatorKind.Implicit => "op_Implicit",
        ConversionOperatorKind.Explicit => "op_Explicit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Conversion kind has no name.")
    };

    private static readonly Dictionary<string, ConversionOperatorKind> ConversionKindsByName =
        Enum.GetValues<ConversionOperatorKind>().ToDictionary(ConversionName, StringComparer.Ordinal);

    /// <summary>The conversion a member named <paramref name="name"/> encodes, or null when the name is no conversion's.</summary>
    public static ConversionOperatorKind? ConversionKindOf(string name) =>
        ConversionKindsByName.TryGetValue(name, out var kind) ? kind : null;
}
