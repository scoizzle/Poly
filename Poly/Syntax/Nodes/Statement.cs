namespace Poly.Syntax.Nodes;

/// <summary>
/// Base type for AST nodes that NEVER push a value onto the eval stack.
/// Examples: WhileLoop, IfStatement, Return, Break, Continue, Throw.
/// </summary>
public abstract record Statement : Node;