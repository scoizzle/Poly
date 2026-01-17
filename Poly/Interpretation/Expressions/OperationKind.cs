namespace Poly.Interpretation;

/// <summary>
/// Well-defined binary operations used by interpreters.
/// </summary>
public enum BinaryOperationKind {
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    And,
    Or,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Coalesce
}

/// <summary>
/// Well-defined unary operations used by interpreters.
/// </summary>
public enum UnaryOperationKind {
    Negate,
    Not
}