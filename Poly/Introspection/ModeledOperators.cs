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

/// <summary>
/// Poly's modeled-type operator convention: a static member named <c>op_*</c> in <see cref="ITypeDefinition.Methods"/>,
/// the same convention <c>op_Implicit</c> and <c>op_Explicit</c> use. A convention of Poly, not a platform fact.
/// </summary>
internal static class ModeledOperators {
    /// <summary>
    /// The member name for <paramref name="kind"/>, or null when the kind has no declared form
    /// (<see cref="OperatorKind.And"/> and <see cref="OperatorKind.Or"/> are intrinsic only: <c>op_BitwiseAnd</c>
    /// and <c>op_BitwiseOr</c> are <see cref="OperatorKind.BitwiseAnd"/> and <see cref="OperatorKind.BitwiseOr"/>).
    /// </summary>
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

    public static IEnumerable<DeclaredOperator> Find(ITypeDefinition definition, OperatorKind kind) {
        ArgumentNullException.ThrowIfNull(definition);
        var arity = OperatorShape.ArityOf(kind);
        if (NameOf(kind) is not { } name)
            return [];
        return [.. definition.Methods
            .Where(method => method.IsStatic
                && string.Equals(method.Name, name, StringComparison.Ordinal)
                && method.Parameters.Count() == arity)
            .Select(static method => new DeclaredOperator(method, PlatformApplied: false))];
    }
}