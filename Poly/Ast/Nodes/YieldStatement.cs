namespace Poly.Ast.Nodes;

/// <summary>
/// Represents <c>yield return value;</c> or, when <see cref="Value"/> is null, <c>yield break;</c>.
/// </summary>
/// <remarks>
/// Legal only in the body of a method returning <c>IEnumerable</c> / <c>IEnumerable&lt;T&gt;</c>
/// (see <c>IteratorLegalityAnalyzer</c>). The VM does not execute it yet and fails closed.
/// </remarks>
public sealed record YieldStatement(Node? Value = null) : Statement {
    public override IEnumerable<Node?> Children => Value is not null ? [Value] : Enumerable.Empty<Node>();

    /// <inheritdoc />
    public override string ToString() => Value is not null ? $"yield return {Value};" : "yield break;";
}