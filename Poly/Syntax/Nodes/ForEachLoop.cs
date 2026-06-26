namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a for-each loop statement that iterates over a collection, assigning each element to a loop variable and executing a body.
/// </summary>
/// <param name="LoopVariable">The loop variable that will hold each element of the collection during iteration.</param>
/// <param name="Collection">The collection to iterate over.</param>
/// <param name="Body">The body of the loop that will be executed for each element.</param>
public sealed record ForEachLoop(Variable LoopVariable, Node Collection, Node Body) : Statement {
    public override IEnumerable<Node?> Children => [Collection, Body];

    /// <inheritdoc />
    public override string ToString() {
        return $"foreach (var {LoopVariable.Name} in {Collection}) {{ {Body} }}";
    }

    /// <inheritdoc />
    public override IEnumerable<Poly.Syntax.Primitives.PrimitiveNode> ToPrimitives(Analysis.AnalysisContext context) {
        // ForEachLoop requires enumerator pattern support (GetEnumerator/MoveNext/Current).
        // For now, emit collection (for side effects) then body.
        foreach (var p in Collection.ToPrimitives(context))
            yield return p;
        yield return new Poly.Syntax.Primitives.Discard();
        foreach (var p in Body.ToPrimitives(context))
            yield return p;
    }
}