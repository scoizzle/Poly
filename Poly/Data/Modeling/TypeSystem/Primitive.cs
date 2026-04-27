namespace Poly.Data.Modeling.TypeSystem;

public sealed record Primitive : DomainType {
    public Primitive(Domain domain, string name, TypeCategory category) : base(domain) {
        Name = name;
        Category = category;
    }

    public TypeCategory Category { get; }
    public override IReadOnlyCollection<Property> Properties { get; } = [];
}