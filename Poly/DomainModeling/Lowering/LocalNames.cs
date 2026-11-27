namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Shared source of unique local names for one method's lowering. Every name carries the reserved <c>__</c> prefix, so
/// no author name can collide with a lowered local (analysis refuses an author name beginning <c>__</c>, DMSTR009).
/// </summary>
public sealed class LocalNames {
    private int _next;
    public Variable Next(string prefix) => new($"__{prefix}{_next++}");
}