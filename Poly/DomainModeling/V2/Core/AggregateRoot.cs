namespace Poly.DomainModeling.V2.Core;

public sealed record AggregateRoot {
    public SemanticId SemanticId { get; }
    public string Name { get; }

    public AggregateRoot(SemanticId semanticId, string name)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
    }
}