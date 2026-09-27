using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>An expression lowered to statements (loops, short-circuit) plus the value those statements produce.</summary>
public sealed record LoweredExpression(
    IReadOnlyList<Node> Statements,
    IReadOnlyList<Variable> Variables,
    Node Value) {
    /// <summary>An expression with no preceding statements.</summary>
    public static LoweredExpression Of(Node value) => new([], [], value);

    /// <summary>Places these statements immediately before <paramref name="statement"/>.</summary>
    public Node Before(Node statement) => Before([this], statement);

    /// <summary>Places the statements of every part, in order, immediately before <paramref name="statement"/>.</summary>
    public static Node Before(IReadOnlyList<LoweredExpression> parts, Node statement) {
        if (parts.All(p => p.Statements.Count == 0))
            return statement;
        if (statement is Block block)
            return new Block(
                [.. parts.SelectMany(p => p.Statements), .. block.Nodes],
                [.. parts.SelectMany(p => p.Variables), .. block.Variables]);
        return new Block(
            [.. parts.SelectMany(p => p.Statements), statement],
            [.. parts.SelectMany(p => p.Variables)]);
    }

    /// <summary>Concatenates statements and variables from several expressions, using <paramref name="value"/>.</summary>
    public static LoweredExpression Combine(IEnumerable<LoweredExpression> parts, Node value) =>
        new(
            [.. parts.SelectMany(p => p.Statements)],
            [.. parts.SelectMany(p => p.Variables)],
            value);
}
