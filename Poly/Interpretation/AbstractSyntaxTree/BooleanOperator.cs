namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Base record for operators that produce boolean results.
/// </summary>
/// <remarks>
/// All boolean operators (logical operations, comparisons, equality tests) inherit from this record
/// and are guaranteed to return a boolean type definition.
/// Note: Type information is now resolved by semantic analysis middleware, not by the record itself.
/// </remarks>
public abstract record BooleanOperator : Operator
{
}