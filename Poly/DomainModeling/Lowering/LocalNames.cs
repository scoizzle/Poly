using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>Shared source of unique local names for one method's lowering.</summary>
public sealed class LocalNames {
    private int _next;
    public Variable Next(string prefix) => new($"{prefix}{_next++}");
}
