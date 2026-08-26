namespace Poly.Ast.Nodes;

/// <summary>
/// A named local binding. Same node identity is the slot; live contents are in the VM frame,
/// not on this node. <see cref="Assignment"/> is the write.
/// </summary>
/// <param name="Name">Binding name (shadowing is by nested scope, not by renaming this node).</param>
/// <param name="Initializer">Optional declare-init expression for the first statement-form
/// encounter (<c>var x = e</c>). Not the current value; later reads ignore it.</param>
public sealed record Variable(string Name, Node? Initializer = null) : Expression {
    public override IEnumerable<Node?> Children => [Initializer];

    /// <inheritdoc />
    public override string ToString() => Name;
}