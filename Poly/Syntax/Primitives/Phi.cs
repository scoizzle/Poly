namespace Poly.Syntax.Primitives;

/// <summary>
/// Phi marks a control-flow merge point where the branch-aware ring
/// analysis has ensured both predecessors leave their value at the same
/// ring depth.  StackEffect is (0,0) — a no-op annotation, no runtime
/// code generated.
/// </summary>
public sealed record Phi : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 0);

    public override string ToString() => $"Phi";
}