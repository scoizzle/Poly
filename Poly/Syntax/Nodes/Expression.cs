using Poly.Syntax.Primitives;

namespace Poly.Syntax.Nodes;

/// <summary>
/// Base type for AST nodes that ALWAYS push exactly one value onto the eval stack.
/// Examples: Constant, Add, Conditional, Invoke, Assignment, Member, Lambda.
/// </summary>
public abstract record Expression : Node {
    /// <summary>
    /// Shared helper for binary expression nodes that decompose to a single
    /// <see cref="BinaryOp"/> primitive. Emits left, then right, then the op.
    /// </summary>
    protected static IEnumerable<PrimitiveNode> EmitBinaryOp(
        Node left, Node right, OpKind op, ExpansionContext context) {
        foreach (var p in left.ToPrimitives(context)) yield return p;
        foreach (var p in right.ToPrimitives(context)) yield return p;
        yield return new BinaryOp(op);
    }
}