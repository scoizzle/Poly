namespace Poly.DomainModeling.V2.Core;

public sealed record BoundedContext {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string? Description { get; }

    public BoundedContext(SemanticId semanticId, string name, string? description = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        Description = description;
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record BoundedContext {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string? Description { get; }

    public BoundedContext(SemanticId semanticId, string name, string? description = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("BoundedContext name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        Description = description;
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record BoundedContext {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string? Description { get; }

    public BoundedContext(SemanticId semanticId, string name, string? description = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("BoundedContext name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        Description = description;
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record BoundedContext {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string? Description { get; }

    public BoundedContext(SemanticId semanticId, string name, string? description = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("BoundedContext name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        Description = description;
    }
}