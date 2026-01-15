using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Base class for operators that produce boolean results.
/// </summary>
/// <remarks>
/// All boolean operators (logical operations, comparisons, equality tests) inherit from this class
/// and are guaranteed to return a boolean type definition.
/// </remarks>
public abstract class BooleanOperator : Operator {
    /// <inheritdoc />
    /// <returns>The type definition for <see cref="bool"/>.</returns>
    public sealed override ITypeDefinition GetTypeDefinition(InterpretationContext context)
    {
        return context.GetTypeDefinition<bool>()
            ?? throw new InvalidOperationException("Type 'bool' is not registered in the context.");
    }
}