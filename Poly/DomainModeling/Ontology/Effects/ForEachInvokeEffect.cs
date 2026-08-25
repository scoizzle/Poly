namespace Poly.DomainModeling.Ontology.Effects;

/// <summary>
/// The per-record predicate for a <see cref="ForEachInvokeEffect"/>: a named policy on
/// the iterated (target) entity, or a stage membership check.
/// </summary>
public abstract record ForEachPredicate : DomainObject;

/// <summary>Filters to records for which the named policy on the target entity is true.</summary>
public sealed record ForEachNamedPolicy(string PolicyName) : ForEachPredicate {
    public override IEnumerable<Node?> Children => [];
}

/// <summary>Filters to records currently in the named stage on the target entity.</summary>
public sealed record ForEachStageMembership(string StageName) : ForEachPredicate {
    public override IEnumerable<Node?> Children => [];
}

/// <summary>
/// Iterates every record reachable via a OneToMany relationship (fetch-all from storage)
/// and invokes an action on each, binding the current record to <see cref="BinderName"/>.
///
/// Semantics (fail-fast, single fan-out mode):
/// <list type="bullet">
///   <item>Only the relationship <b>source</b> may author <c>for</c>; OneToOne is rejected.</item>
///   <item>The optional <see cref="Predicate"/> is a <b>named policy</b> or <b>stage
///     membership</b> on the target entity — never an inline expression.</item>
///   <item>Every matching record is invoked in storage order; the <b>first failure fails
///     the whole <c>for</c></b> (no silent swallow).</item>
///   <item><b>Zero matches fail</b> (no vacuous success).</item>
/// </list>
/// </summary>
public sealed record ForEachInvokeEffect(
    string RelationshipName,
    string BinderName,
    ForEachPredicate? Predicate,
    string ActionName,
    IReadOnlyList<PropertyBinding> ParameterBindings
) : Effect {
    public override IEnumerable<Node?> Children => [.. ParameterBindings];
}