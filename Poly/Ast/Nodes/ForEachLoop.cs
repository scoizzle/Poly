namespace Poly.Ast.Nodes;

/// <summary>
/// Represents a for-each loop statement that iterates over a collection, assigning each element to a loop variable and executing a body.
/// </summary>
/// <param name="LoopVariable">The loop variable that will hold each element of the collection during iteration.</param>
/// <param name="Collection">The collection to iterate over.</param>
/// <param name="Body">The body of the loop that will be executed for each element.</param>
/// <param name="Label">Optional label on the loop.</param>
/// <param name="DeclaredType">The loop variable's declared element type (<c>foreach (double d in …)</c>);
/// null for the <c>var</c> form, where the element type wins.</param>
public sealed record ForEachLoop(Variable LoopVariable, Node Collection, Node Body, string? Label = null, Node? DeclaredType = null) : Statement {
    public override IEnumerable<Node?> Children => [Collection, Body, DeclaredType];

    /// <inheritdoc />
    public override string ToString() {
        var decl = DeclaredType is null ? $"var {LoopVariable.Name}" : $"{DeclaredType} {LoopVariable.Name}";
        return $"foreach ({decl} in {Collection}) {{ {Body} }}";
    }

}