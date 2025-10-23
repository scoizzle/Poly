namespace Poly.Interpretation;

/// <summary>
/// Represents an operator in an interpretation tree that performs operations on values.
/// </summary>
/// <remarks>
/// Operators are values that combine, compare, or transform other values. This includes
/// arithmetic operations, logical operations, comparisons, assignments, and member access.
/// This abstract class serves as a marker to distinguish operations from simple values.
/// </remarks>
public abstract class Operator : Value {
}