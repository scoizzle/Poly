namespace Poly.Ast.Nodes;

/// <summary>
/// AST node representing a single attribute application, e.g. <c>[Key]</c> or <c>[MaxLength(100)]</c>.
/// </summary>
/// <param name="Name">The attribute name (without "Attribute" suffix).</param>
/// <param name="Arguments">Positional constructor arguments to the attribute.</param>
public sealed record AttributeNode(
    string Name,
    IReadOnlyList<Expression> Arguments
) : Node {
    public override IEnumerable<Node?> Children {
        get {
            foreach (var a in Arguments) yield return a;
        }
    }

    public override string ToString() {
        var args = Arguments.Count > 0 ? $"({string.Join(", ", Arguments)})" : "";
        return $"[{Name}{args}]";
    }
}

/// <summary>
/// Decorator node that wraps another node with a list of attributes.
/// During code generation, the attributes are emitted before the wrapped node.
/// </summary>
/// <param name="Inner">The node being attributed.</param>
/// <param name="Attributes">Attributes to apply.</param>
public sealed record AttributedNode(
    Node Inner,
    IReadOnlyList<AttributeNode> Attributes
) : Node {
    public override IEnumerable<Node?> Children {
        get {
            yield return Inner;
            foreach (var a in Attributes) yield return a;
        }
    }

    public override string ToString() {
        var attrs = string.Join(" ", Attributes);
        return $"{attrs} {Inner}";
    }
}