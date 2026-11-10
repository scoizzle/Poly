namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// The general <c>for x in Rel [where cond] { effects }</c> effect (#409): iterates the
/// records of a <c>many</c> relationship on the source entity, binding each to
/// <see cref="BinderName"/>, optionally filtered by an inline <see cref="Where"/> condition,
/// and runs <see cref="Body"/> per record. Distinct from <see cref="ForEachInvokeEffect"/>
/// (<c>for Rel as x … invoke x.Action</c>), which stays the named-predicate fan-out.
///
/// Lowered to a fail-fast loop in <c>EffectLoweringPass.ForEach</c>; zero records is a no-op.
/// </summary>
public sealed record ForEachEffect(
    string BinderName,
    string RelationshipName,
    DomainExpression? Where,
    IReadOnlyList<Effect> Body
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Where, .. Body];
}