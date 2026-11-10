namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>
/// The orderings a property may have against another property: a non-empty subset of
/// {less, equal, greater}. <c>&lt;</c> is <see cref="Less"/>, <c>&lt;=</c> is
/// <c>Less | Equal</c>, <c>!=</c> is <c>Less | Greater</c>; <see cref="All"/> is no information.
/// </summary>
[Flags]
public enum RelationalOrdering {
    None = 0,
    Less = 1,
    Equal = 2,
    Greater = 4,
    All = Less | Equal | Greater,
}

/// <summary>
/// A relation between the property it constrains and <see cref="Other"/> (a property of the same
/// entity): "this property is <see cref="Orderings"/> than Other". It is derived by analysis from
/// a policy that compares two properties (<c>Start &lt; End</c>); it has no DSL word (#367 A-6).
/// The meet of two relations over the same pair is the intersection of their ordering sets
/// (empty is bottom); the join is the union (all three is top). Relations over different
/// pairs are independent facts.
/// </summary>
public sealed record RelationalConstraint(string Other, RelationalOrdering Orderings) : Constraint {
    /// <summary>The same relation seen from the other property: <c>a &lt; b</c> is <c>b &gt; a</c>.</summary>
    public RelationalConstraint MirroredFrom(string owner) => new(owner, Mirror(Orderings));

    internal static RelationalOrdering Mirror(RelationalOrdering o) =>
        (o & RelationalOrdering.Equal)
        | ((o & RelationalOrdering.Less) != 0 ? RelationalOrdering.Greater : 0)
        | ((o & RelationalOrdering.Greater) != 0 ? RelationalOrdering.Less : 0);

    internal static string Symbol(RelationalOrdering o) => o switch {
        RelationalOrdering.Less => "<",
        RelationalOrdering.Less | RelationalOrdering.Equal => "<=",
        RelationalOrdering.Greater => ">",
        RelationalOrdering.Greater | RelationalOrdering.Equal => ">=",
        RelationalOrdering.Equal => "==",
        RelationalOrdering.Less | RelationalOrdering.Greater => "!=",
        _ => "any",
    };
}