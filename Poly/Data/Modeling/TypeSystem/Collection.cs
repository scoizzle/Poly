namespace Poly.Data.Modeling.TypeSystem;

public sealed record Collection(Domain Domain, string Name, DomainType ElementType) : DomainType(Domain, Name) {
    public override IEnumerable<DomainObject> ChildObjects => [ElementType];
}