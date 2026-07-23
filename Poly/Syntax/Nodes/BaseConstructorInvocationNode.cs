namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a base constructor invocation, e.g. <c>: base(args)</c>.
/// </summary>
/// <param name="Arguments">Arguments passed to the base constructor.</param>
public sealed record BaseConstructorInvocationNode(
    IReadOnlyList<Expression> Arguments
) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var a in Arguments) yield return a;
        }
    }

    public override string ToString() {
        var args = string.Join(", ", Arguments);
        return $"base({args})";
    }
}