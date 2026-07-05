namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a label declaration that marks a location for goto statements.
/// </summary>
/// <remarks>
/// A label marks a location in code that can be targeted by goto statements.
/// Labels enable non-local control transfers within a function scope.
/// </remarks>
public sealed record LabelDeclaration(string Name, Node Statement) : Statement {
    public override IEnumerable<Node?> Children => [Statement];

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Statement}";

    /// <inheritdoc />
    public override IEnumerable<Primitives.PrimitiveNode> ToPrimitives(Primitives.ExpansionContext context) {
        yield return new Primitives.Label(Name);
        foreach (var p in Statement.ToPrimitives(context)) yield return p;
    }
}