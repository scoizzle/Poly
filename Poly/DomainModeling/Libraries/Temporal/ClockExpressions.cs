using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// The current clock instant (UTC). Lowers to <c>DateTime.UtcNow</c> (static
/// member on a type name); the VM executes that tree.
/// </summary>
public sealed record Now : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}

/// <summary>
/// Today's date (UTC). Lowers to <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>.
/// </summary>
public sealed record Today : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}