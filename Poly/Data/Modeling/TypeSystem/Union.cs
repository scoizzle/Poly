namespace Poly.Data.Modeling.TypeSystem;

public sealed record Union(Domain Domain, string Name) : DomainType(Domain, Name) {
    public IReadOnlyCollection<DomainType> Options { get; init; } = [];

    public override IEnumerable<DomainObject> ChildObjects => Options;
}