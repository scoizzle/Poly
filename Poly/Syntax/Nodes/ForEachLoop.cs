namespace Poly.Syntax.Nodes;

/// <summary>
/// Represents a for-each loop statement that iterates over a collection, assigning each element to a loop variable and executing a body.
/// </summary>
/// <param name="LoopVariable">The loop variable that will hold each element of the collection during iteration.</param>
/// <param name="Collection">The collection to iterate over.</param>
/// <param name="Body">The body of the loop that will be executed for each element.</param>
public sealed record ForEachLoop(Variable LoopVariable, Node Collection, Node Body, string? Label = null) : Statement {
    public override IEnumerable<Node?> Children => [Collection, Body];

    /// <inheritdoc />
    public override string ToString() {
        return $"foreach (var {LoopVariable.Name} in {Collection}) {{ {Body} }}";
    }

}