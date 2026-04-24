namespace Poly.Data.Modeling.TypeSystem;

public sealed class Optional : IDomainType {
    public required Domain Domain { get; init; }
    public required string Name { get; init; }
    public required IDomainType UnderlyingType { get; init; }
    public IReadOnlyCollection<Property> Properties { get; init; } = [];
}