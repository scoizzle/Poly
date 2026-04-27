namespace Poly.Data.Modeling.TypeSystem;

public sealed record Collection : DomainType {
    public Collection(Domain domain, string name, IDomainType elementType) : base(domain) {
        Name = name;
        ElementType = elementType;
    }

    public IDomainType ElementType { get; }
    public override IReadOnlyCollection<Property> Properties { get; } = [];
}