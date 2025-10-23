namespace Poly.Interpretation;

/// <summary>
/// Base class for objects that can be interpreted and compiled into LINQ Expression Trees.
/// </summary>
/// <remarks>
/// This is the root of the interpretation hierarchy, providing a unified way to build
/// expression trees from a higher-level abstract syntax. Implementations typically
/// represent literals, variables, parameters, or operations that can be converted
/// into executable code via System.Linq.Expressions.
/// </remarks>
public abstract class Interpretable {
    /// <summary>
    /// Builds a LINQ Expression Tree representation of this interpretable object.
    /// </summary>
    /// <param name="context">The interpretation context containing type definitions, variables, and parameters.</param>
    /// <returns>An <see cref="Expression"/> that represents this interpretable in the expression tree.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the expression cannot be built due to missing type information or invalid state.</exception>
    public abstract Expression BuildExpression(InterpretationContext context);
}