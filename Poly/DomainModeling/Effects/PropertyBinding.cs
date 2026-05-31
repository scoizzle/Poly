namespace Poly.DomainModeling.Effects;

/// <summary>
/// Binds a property on a domain object to a <see cref="DomainExpression"/>.
/// </summary>
public sealed record PropertyBinding(
    string PropertyName,
    DomainExpression Expression
) : DomainObject {
    public sealed override IEnumerable<Node?> Children => [Expression];
}