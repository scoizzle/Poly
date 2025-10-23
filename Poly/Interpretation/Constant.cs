namespace Poly.Interpretation;

/// <summary>
/// Represents a constant value in an interpretation tree.
/// </summary>
/// <remarks>
/// Constants are immutable values that are known at interpretation time and compile
/// to <see cref="System.Linq.Expressions.ConstantExpression"/> nodes. This abstract class
/// serves as a marker to distinguish constant values from mutable variables or parameters.
/// </remarks>
public abstract class Constant : Value {
}