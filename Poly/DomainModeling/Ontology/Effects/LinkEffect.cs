namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// <c>link Rel to binder</c> (#407, F-16): attaches the instance bound to <see cref="Target"/>
/// (a <c>for</c> binder) to the relationship <see cref="RelationshipName"/> of the effect's entity.
/// Syntax, IR and analysis only: Analyze refuses every use (DMEFF020) until L-13 lowers it.
/// </summary>
public sealed record LinkEffect(
    string RelationshipName,
    DomainExpression Target
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Target];
}
