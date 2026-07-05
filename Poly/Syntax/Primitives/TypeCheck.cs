namespace Poly.Syntax.Primitives;

/// <summary>
/// Runtime type check: pops a heap handle, checks whether the referenced
/// object is an instance of the specified CLR type, pushes 0 or 1.
/// This is the minimal viable primitive for TypeIs lowering (INT-020).
/// </summary>
/// <param name="TargetType">The CLR type to check against.</param>
public sealed record TypeCheck(System.Type TargetType) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 1);
}