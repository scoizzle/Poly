namespace Poly.Data.Modeling.TypeSystem;

public sealed class Union : IDomainType {
    public required Domain Domain { get; init; }
    public required string Name { get; init; }
    public IReadOnlyCollection<IDomainType> Options { get; init; } = [];
    public IReadOnlyCollection<Property> Properties { get; init; } = [];
}