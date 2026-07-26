namespace Poly.Ast.Nodes;

/// <summary>
/// Base type for AST nodes that ALWAYS push exactly one value onto the eval stack.
/// Examples: Constant, Add, Conditional, Invoke, Assignment, Member, Lambda.
/// </summary>
public abstract record Expression : Node {
}