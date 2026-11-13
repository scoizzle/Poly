namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// C# constant-expression conversion (spec 10.2.11), which the type provider does not model yet: a non-negative
/// constant of <c>sbyte</c>, <c>short</c>, <c>int</c> or <c>long</c> converts to <c>ulong</c>, so beside a <c>ulong</c>
/// operand the pair is judged as UInt64 (<c>ulong x; x &gt; 1</c>, <c>100UL + 50</c>). A negative constant stays an
/// error, as in C#. The rule inserts no node. Owner ruling #679 6842272727; removed by T-26.
/// </summary>
internal static class ConstantOperandRule {
    /// <summary>
    /// True when <paramref name="kind"/> is one of the five arithmetic or six comparison kinds and one operand is a
    /// non-negative signed integral constant while the other is <c>ulong</c>, <c>ulong?</c> or a <c>ulong</c> constant.
    /// <paramref name="left"/> and <paramref name="right"/> are the effective operand nodes (after replacement).
    /// </summary>
    public static bool Applies(OperatorKind kind, Node left, Type? leftType, Node right, Type? rightType) =>
        kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo
            or OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual
        && ((IsNonNegativeSignedConstant(left) && IsUInt64Operand(right, rightType))
            || (IsNonNegativeSignedConstant(right) && IsUInt64Operand(left, leftType)));

    private static bool IsNonNegativeSignedConstant(Node node) => node switch {
        Constant { Value: sbyte value } => value >= 0,
        Constant { Value: short value } => value >= 0,
        Constant { Value: int value } => value >= 0,
        Constant { Value: long value } => value >= 0,
        _ => false
    };

    private static bool IsUInt64Operand(Node node, Type? type) =>
        type == typeof(ulong) || type == typeof(ulong?) || node is Constant { Value: ulong };
}