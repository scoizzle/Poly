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
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Emit RHS
        foreach (var p in Value.ToPrimitives(context))
            yield return p;

        if (Destination is Variable destVar) {
            var env = context.Env;
            if (!env.TryGetSlot(destVar, out var slot))
                throw new InvalidOperationException($"Variable '{destVar.Name}' has no slot assigned");
            // StoreLocal (1,0) consumes the RHS value from the ring.
            // When inside a lambda and the variable is captured, emit
            // StoreUpvalue instead.  In expression context the value
            // must be preserved — Dup before the store keeps a copy.
            if (env.IsInExpressionContext)
                yield return new Primitives.Dup();
            if (env.IsUpvalue(destVar)) {
                int upvIdx = env.GetOrAssignUpvalueIndex(destVar);
                yield return new Primitives.StoreUpvalue(upvIdx);
            }
            else {
                yield return new Primitives.StoreLocal(slot);
            }

        }
        else if (Destination is IndexAccess indexAccess) {
            // arr[i] = value: emit array handle, index, then ArrayStore
            foreach (var p in indexAccess.Value.ToPrimitives(context))
                yield return p;
            foreach (var arg in indexAccess.Arguments)
                foreach (var p in arg.ToPrimitives(context))
                    yield return p;
            yield return new Primitives.ArrayStore();
            // Re-load value for expression semantics (dup the RHS)
            // RHS value is still on the virtual ring from the first emit
        }
        else {
            throw new NotSupportedException($"Assignment destination type not supported: {Destination.GetType().Name}");
        }
    }
}