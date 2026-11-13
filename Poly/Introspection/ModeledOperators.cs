namespace Poly.Introspection;

/// <summary>
/// How many operands an <see cref="OperatorKind"/> takes. An exhaustive switch with a throwing default,
/// so a kind added later (unary minus, modulo) fails loudly until it is classified here.
/// </summary>
internal static class OperatorShape {
    public static int ArityOf(OperatorKind kind) => kind switch {
        OperatorKind.Not => 1,
        OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide
            or OperatorKind.Equal or OperatorKind.NotEqual
            or OperatorKind.LessThan or OperatorKind.LessThanOrEqual
            or OperatorKind.GreaterThan or OperatorKind.GreaterThanOrEqual
            or OperatorKind.And or OperatorKind.Or => 2,
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
    /// (<see cref="OperatorKind.And"/> and <see cref="OperatorKind.Or"/> are intrinsic only: the CLR's
    /// <c>op_BitwiseAnd</c> is not logical-and).
    /// </summary>
    public static string? NameOf(OperatorKind kind) => kind switch {
        OperatorKind.Add => "op_Addition",
        OperatorKind.Subtract => "op_Subtraction",
        OperatorKind.Multiply => "op_Multiply",
        OperatorKind.Divide => "op_Division",
        OperatorKind.Equal => "op_Equality",
        OperatorKind.NotEqual => "op_Inequality",
        OperatorKind.LessThan => "op_LessThan",
        OperatorKind.LessThanOrEqual => "op_LessThanOrEqual",
        OperatorKind.GreaterThan => "op_GreaterThan",
        OperatorKind.GreaterThanOrEqual => "op_GreaterThanOrEqual",
        OperatorKind.Not => "op_LogicalNot",
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