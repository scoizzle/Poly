namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// C# constant-expression conversion (spec 10.2.11), which the type provider does not model yet: a non-negative
/// constant of <c>sbyte</c>, <c>short</c>, <c>int</c> or <c>long</c> converts to <c>ulong</c>, so beside a <c>ulong</c>
/// operand the pair is judged as UInt64 (<c>ulong x; x &gt; 1</c>, <c>100UL + 50</c>). The same holds for a non-negative
/// <c>int</c> or <c>long</c> constant beside <c>nuint</c> (<c>nuint x; x + 1</c>; C# 11, checked against Roslyn by
/// <c>NativeIntegerTests</c>), which is judged as UIntPtr. A negative constant stays an error, as in C#. The rule
/// inserts no node. Owner ruling #679 6842272727; removed by T-26. T-22a adds the three bitwise kinds (10.2.11 applies to
/// every binary operator; T-22 lock amendment 2): <c>ulong x; x &amp; 1</c>.
/// </summary>
internal static class ConstantOperandRule {
    /// <summary>
    /// True when <paramref name="kind"/> is one of the five arithmetic, six comparison or three bitwise kinds and one
    /// operand is a non-negative signed integral constant while the other is <c>ulong</c>, <c>ulong?</c> or a <c>ulong</c>
    /// constant. <paramref name="left"/> and <paramref name="right"/> are the effective operand nodes (after replacement).
    /// </summary>
    public static bool Applies(OperatorKind kind, Node left, Type? leftType, Node right, Type? rightType) =>
        kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo
            or OperatorKind.Equal or OperatorKind.NotEqual or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual
            or OperatorKind.BitwiseAnd or OperatorKind.BitwiseOr or OperatorKind.BitwiseXor
        && ((IsNonNegativeSignedConstant(left) && IsUInt64Operand(right, rightType))
            || (IsNonNegativeSignedConstant(right) && IsUInt64Operand(left, leftType))
            || (IsNonNegativeIntOrLongConstant(left) && IsNativeUnsignedOperand(right, rightType))
            || (IsNonNegativeIntOrLongConstant(right) && IsNativeUnsignedOperand(left, leftType)));

    private static bool IsNonNegativeSignedConstant(Node node) => node switch {
        Constant { Value: sbyte value } => value >= 0,
        Constant { Value: short value } => value >= 0,
        Constant { Value: int value } => value >= 0,
        Constant { Value: long value } => value >= 0,
        _ => false
    };

    private static bool IsUInt64Operand(Node node, Type? type) =>
        type == typeof(ulong) || type == typeof(ulong?) || node is Constant { Value: ulong };

    private static bool IsNonNegativeIntOrLongConstant(Node node) => node switch {
        Constant { Value: int value } => value >= 0,
        Constant { Value: long value } => value >= 0,
        _ => false
    };

    private static bool IsNativeUnsignedOperand(Node node, Type? type) =>
        type == typeof(nuint) || type == typeof(nuint?) || node is Constant { Value: nuint };
}