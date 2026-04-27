namespace Poly.Data.Modeling.TypeSystem;

public sealed record Union : DomainType {
    public Union(Domain domain, string name) : base(domain) {
        Name = name;
    }

    public IReadOnlyCollection<IDomainType> Options { get; init; } = [];
    public override IReadOnlyCollection<Property> Properties { get; } = [];
}