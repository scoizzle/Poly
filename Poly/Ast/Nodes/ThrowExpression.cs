namespace Poly.Ast.Nodes;

/// <summary>
/// A <c>throw</c> in expression position (C# 7+ throw-expression), e.g.
/// <c>this.Book ?? throw new InvalidOperationException("...")</c>. Used by the exporter
/// to turn an unlinked relationship navigation into a deliberate, message-carrying
/// failure instead of a null-dereference crash.
/// </summary>
public sealed record ThrowExpression(Expression Value) : Expression {
    public override IEnumerable<Node?> Children => [Value];

    /// <inheritdoc />
    public override string ToString() => $"throw {Value}";
}