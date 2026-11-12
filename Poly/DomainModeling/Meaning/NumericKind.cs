namespace Poly.DomainModeling.Meaning;

/// <summary>The numeric family of an <see cref="ExprType"/>; <see cref="None"/> for every non-numeric type.</summary>
internal enum NumericKind {
    None,
    Integer64,
    Decimal,
    Float64
}