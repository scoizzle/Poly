using Poly.Introspection;

namespace Poly.Interpretation.Operators;

public abstract class BooleanOperator : Operator {
    public sealed override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        return context.GetTypeDefinition<bool>()
            ?? throw new InvalidOperationException("Type 'bool' is not registered in the context.");
    }
}