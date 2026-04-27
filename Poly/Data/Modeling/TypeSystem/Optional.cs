namespace Poly.Data.Modeling.TypeSystem;

public sealed record Optional : DomainType {
    public Optional(Domain domain, string name, IDomainType underlyingType) : base(domain) {
        Name = name;
        UnderlyingType = underlyingType;
    }

    public IDomainType UnderlyingType { get; }
    public override IReadOnlyCollection<Property> Properties { get; } = [];
}