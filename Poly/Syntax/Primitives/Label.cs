namespace Poly.Syntax.Primitives;

/// <summary>
/// A no-op label marker primitive used as a branch target by <see cref="Goto"/> and <see cref="CondGoto"/>.
/// </summary>
/// <param name="Name">Optional debug name for trace output.</param>
public sealed record Label(string Name) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 0);

    /// <inheritdoc />
    public override string ToString() => Name ?? string.Empty;
}