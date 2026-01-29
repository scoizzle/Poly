namespace Poly.Interpretation.AbstractSyntaxTree;

/// <summary>
/// Represents a label declaration that marks a location for goto statements.
/// </summary>
/// <remarks>
/// A label marks a location in code that can be targeted by goto statements.
/// Labels enable non-local control transfers within a function scope.
/// </remarks>
public sealed record LabelDeclaration(string Name, Node Statement) : Operator {
    public override IEnumerable<Node?> Children => [Statement];

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {Statement}";
}