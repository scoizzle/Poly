namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents an anonymous function (lambda expression) in an interpretation tree.
/// </summary>
/// <remarks>
/// Defines a callable unit with a parameter list and a body expression. Compiles to a
/// <see cref="Exprs.LambdaExpression"/> which can be invoked via
/// <see cref="Invoke"/> or compiled into a delegate.
/// <para>
/// Lambda nodes introduce a new return scope: <see cref="ReturnStatement.Return"/> nodes
/// inside the body exit this lambda, not any enclosing expression.
/// </para>
/// </remarks>
/// <param name="Parameters">The parameters accepted by this lambda.</param>
/// <param name="Body">The body expression evaluated when the lambda is invoked.</param>
public sealed record Lambda(IReadOnlyList<Parameter> Parameters, Node Body) : Expression {

    /// <summary>Index assigned during expansion, used by <see cref="Invoke"/>
    /// to reference the body function entry.  Set during <c>ToPrimitives()</c>.</summary>
    public int LambdaIndex { get; set; } = -1;
    public override IEnumerable<Node?> Children {
        get {
            foreach (var p in Parameters) yield return p;
            yield return Body;
        }
    }

    public override string ToString() {
        var paramList = string.Join(", ", Parameters);
        return $"({paramList}) => {Body}";
    }

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        // Assign a unique lambda index for Call dispatch before expanding body.
        LambdaIndex = context.Env.AllocateLambdaIndex();

        // Create a child context for lambda body expansion. The child has its
        // own 0-based slot space; references to outer-scope slots become captures.
        var bodyCtx = context.CreateChildScope();

        // Assign parameter slots in the child's space; register names so body
        // Parameter nodes with the same name (distinct instances) share a slot.
        foreach (var param in Parameters) {
            var slot = bodyCtx.Env.GetOrAssignSlot(param);
            bodyCtx.Env.RegisterLambdaParameter(param.Name, slot);
        }

        // Expand body — captures detected automatically via IsUpvalue
        var bodyPrims = new List<Primitives.PrimitiveNode>();
        using (bodyCtx.Env.EnterStatementContext()) {
            foreach (var p in Body.ToPrimitives(bodyCtx))
                bodyPrims.Add(p);
        }

        // Collect captured info: for each upvalue, map child slot → parent slot
        var captures = bodyCtx.Env.GetCaptures();

        // Register as pending function in parent scope
        context.Env.AddPendingFunction(LambdaIndex, bodyPrims, captures,
            Parameters.Count, bodyCtx.Env.LocalSlotCount);

        // Emit capture loads (read from outer frame slots) + AllocClosure
        foreach (var (parentSlot, _) in captures)
            yield return new Primitives.LoadLocal(parentSlot);
        yield return new Primitives.AllocClosure(LambdaIndex, captures.Count);
    }
}