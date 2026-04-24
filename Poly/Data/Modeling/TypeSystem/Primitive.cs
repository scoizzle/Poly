namespace Poly.Data.Modeling.TypeSystem;

public sealed class Primitive : IDomainType {
    public required Domain Domain { get; init; }
    public required string Name { get; init; }
    public TypeCategory Category { get; init; }
    public IReadOnlyCollection<Property> Properties { get; init; } = [];
}