namespace Poly.Ast.Nodes;

/// <summary>
/// A named local binding. Same node identity is the slot; live contents are in the VM frame,
/// not on this node. Declare on <see cref="Block.Variables"/> or as a foreach loop variable.
/// User writes are <see cref="Assignment"/>; foreach writes the loop variable.
/// C# declare-only is <c>var x = default(T)</c> (or <c>T x</c>); <c>var x = e</c> is
/// printer fusion of declare plus a direct first assignment, not a third AST form.
/// </summary>
/// <param name="Name">Binding name (shadowing is by nested scope, not by renaming this node).</param>
public sealed record Variable(string Name) : Expression {
    public override IEnumerable<Node?> Children => [];

    /// <inheritdoc />
    public override string ToString() => Name;
}