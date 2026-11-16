namespace Poly.Introspection;

/// <summary>
/// How many operands an <see cref="OperatorKind"/> takes. An exhaustive switch with a throwing default,
/// so a kind added later fails loudly until it is classified here.
/// </summary>
internal static class OperatorShape {
    public static int ArityOf(OperatorKind kind) => kind switch {
        OperatorKind.Not or OperatorKind.UnaryMinus or OperatorKind.UnaryPlus or OperatorKind.BitwiseNot => 1,
        OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo
            or OperatorKind.Equal or OperatorKind.NotEqual
            or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual
            or OperatorKind.And or OperatorKind.Or
            or OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor
            or OperatorKind.ShiftLeft or OperatorKind.ShiftRight => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Operator kind has no arity.")
    };
}