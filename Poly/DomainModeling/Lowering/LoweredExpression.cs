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
    public Node Before(Node statement) =>
        Statements.Count == 0
            ? statement
            : new Block([.. Statements, statement], Variables);

    /// <summary>Concatenates statements and variables from several expressions, using <paramref name="value"/>.</summary>
    public static LoweredExpression Combine(IEnumerable<LoweredExpression> parts, Node value) =>
        new(
            [.. parts.SelectMany(p => p.Statements)],
            [.. parts.SelectMany(p => p.Variables)],
            value);
}
