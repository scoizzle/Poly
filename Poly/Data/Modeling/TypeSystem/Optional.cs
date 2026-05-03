namespace Poly.Data.Modeling.TypeSystem;

public sealed record Optional(Domain Domain, string Name, DomainType UnderlyingType) : DomainType(Domain, Name) {
    public override IEnumerable<DomainObject> ChildObjects => [UnderlyingType];
}