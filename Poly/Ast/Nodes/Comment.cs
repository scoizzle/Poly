namespace Poly.Ast.Nodes;

/// <summary>
/// A no-op placeholder that carries a human-readable message about what was not lowered.
/// Satisfies <see cref="Block"/>'s non-empty constraint while preserving information
/// about direct-execution effects that were skipped during lowering.
/// The VM should skip this node during execution.
/// </summary>
/// <param name="Text">Human-readable message about what was not lowered.</param>
public sealed record Comment(string Text) : Expression {
    public override string ToString() => $"/* {Text} */";
}