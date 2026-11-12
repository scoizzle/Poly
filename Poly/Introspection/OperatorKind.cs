namespace Poly.Introspection;

/// <summary>
/// The closed set of operators a syntax node can denote, independent of operand types.
/// Syntax→kind lives in <c>OperatorTable</c>; operand and result typing live elsewhere (OQ-11).
/// </summary>
public enum OperatorKind {
    Add,
    Subtract,
    Multiply,
    Divide,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    And,
    Or,
    Not
}