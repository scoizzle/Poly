namespace Poly.DomainModeling;

/// <summary>
/// Policies are used for validation, access control, and business rules. They can be attached at the
/// entity, stage, action, or property level. The rule logic is expressed using the unified
/// <see cref="DomainExpression"/> system.
/// </summary>
public sealed record Policy(
    string Name,
    DomainExpression Expression
) : DomainMember(Name) {
    public sealed override IEnumerable<Node?> Children => [Expression];
}