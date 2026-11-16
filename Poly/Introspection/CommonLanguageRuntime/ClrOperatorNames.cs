namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's special names for operators (ECMA-335 I.10.3): how C# and the CLR encode an operator as a static member.
/// <see cref="OperatorKind.And"/> and <see cref="OperatorKind.Or"/> have no member form: <c>op_BitwiseAnd</c> and
/// <c>op_BitwiseOr</c> are <see cref="OperatorKind.BitwiseAnd"/> and <see cref="OperatorKind.BitwiseOr"/>.
/// </summary>
internal static class ClrOperatorNames {
    /// <summary>The member name for <paramref name="kind"/>, or null when the kind has no member form.</summary>
    public static string? NameOf(OperatorKind kind) => kind switch {
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

    private static readonly Dictionary<string, OperatorKind> KindsByName = Enum.GetValues<OperatorKind>()
        .Where(static kind => NameOf(kind) is not null)
        .ToDictionary(static kind => NameOf(kind)!, StringComparer.Ordinal);

    /// <summary>The operator a member named <paramref name="name"/> encodes, or null when the name is no operator's.</summary>
    public static OperatorKind? KindOf(string name) =>
        KindsByName.TryGetValue(name, out var kind) ? kind : null;
}