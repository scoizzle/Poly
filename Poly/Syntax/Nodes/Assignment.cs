namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an assignment operation that assigns a value to a destination.
/// </summary>
/// <remarks>
/// The destination must be an assignable expression (variable, parameter, member, etc.).
/// Type information is resolved by semantic analysis passes (INodeAnalyzer implementations).
/// </remarks>
public sealed record Assignment(Node Destination, Node Value) : Expression {
    public override IEnumerable<Node?> Children => [Value, Destination];

    /// <inheritdoc />
    public override string ToString() => $"{Destination} = {Value}";

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // Emit RHS
        foreach (var p in Value.ToPrimitives(context))
            yield return p;

        if (Destination is Poly.Syntax.Nodes.Variable destVar) {
            var env = context.GetMetadata<Poly.Syntax.Primitives.ExpandEnv>(null);
            if (env is null || !env.TryGetSlot(destVar, out var slot))
                throw new InvalidOperationException($"Variable '{destVar.Name}' has no slot assigned");
            yield return new Poly.Syntax.Primitives.StoreLocal(slot);
            // Re-load so assignment is an expression
            yield return new Poly.Syntax.Primitives.LoadLocal(slot);
        }
        else if (Destination is Poly.Syntax.Nodes.IndexAccess indexAccess) {
            // arr[i] = value: emit array handle, index, then ArrayStore
            foreach (var p in indexAccess.Value.ToPrimitives(context))
                yield return p;
            foreach (var arg in indexAccess.Arguments)
                foreach (var p in arg.ToPrimitives(context))
                    yield return p;
            yield return new Poly.Syntax.Primitives.ArrayStore();
            // Re-load value for expression semantics (dup the RHS)
            // RHS value is still on the virtual ring from the first emit
        }
        else {
            throw new System.NotSupportedException($"Assignment destination type not supported: {Destination.GetType().Name}");
        }
    }
}