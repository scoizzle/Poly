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
            var env = context.GetMetadata<Poly.Syntax.Primitives.ExpansionEnvironment>(null);
            if (env is null || !env.TryGetSlot(destVar, out var slot))
                throw new InvalidOperationException($"Variable '{destVar.Name}' has no slot assigned");
            // StoreLocal (1,0) consumes the RHS value from the ring.
            // In expression context (StatementDepth == 0) the value must be
            // preserved — Dup before StoreLocal keeps a copy on the ring.
            // In statement context the result is discarded, so a bare
            // StoreLocal suffices.
            if (env.StatementDepth == 0)
                yield return new Poly.Syntax.Primitives.Dup();
            yield return new Poly.Syntax.Primitives.StoreLocal(slot);
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