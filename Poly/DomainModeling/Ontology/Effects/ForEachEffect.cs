namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// The general <c>for x in Rel [where cond] { effects }</c> effect (#409): iterates the
/// records of a <c>many</c> relationship on the source entity, binding each to
/// <see cref="BinderName"/>, optionally filtered by an inline <see cref="Where"/> condition,
/// and runs <see cref="Body"/> per record. Distinct from <see cref="ForEachInvokeEffect"/>
/// (<c>for Rel as x … invoke x.Action</c>), which stays the named-predicate fan-out.
///
/// Slice 4 ships parse and print only. Effect legality (slice 5) and lowering plus runtime
/// (slice 6) are not shipped, so Analyze rejects the form (DMEFF016) rather than passing it.
/// </summary>
public sealed record ForEachEffect(
    string BinderName,
    string RelationshipName,
    DomainExpression? Where,
    IReadOnlyList<Effect> Body
) : Effect {
    public sealed override IEnumerable<Node?> Children => [Where, .. Body];
}