namespace Poly.Syntax.Primitives;

/// <summary>Binary operation kinds supported by the primitive instruction set.</summary>
public enum OpKind {
    Add, Sub, Mul, Div, Mod,
    Gt, Gte, Lt, Lte, Eq, Neq,
    And, Or, Xor, Shl, Shr
}

/// <summary>Unary operation kinds supported by the primitive instruction set.</summary>
public enum UnaryOpKind {
    Neg,
    Not,
    BitNot
}