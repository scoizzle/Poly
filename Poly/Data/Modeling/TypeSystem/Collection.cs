namespace Poly.Data.Modeling.TypeSystem;

public sealed class Collection : IDomainType {
    public required Domain Domain { get; init; }
    public required string Name { get; init; }
    public required IDomainType ElementType { get; init; }
    public IReadOnlyCollection<Property> Properties { get; init; } = [];
}