namespace Poly.DomainModeling.V2.Core;

public sealed record Aggregate {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId BoundedContextId { get; }
    public SemanticId AggregateRootTypeId { get; }
    public IReadOnlyList<SemanticId> DomainTypeIds { get; }

    public Aggregate(
        SemanticId semanticId,
        string name,
        SemanticId boundedContextId,
        SemanticId aggregateRootTypeId,
        IEnumerable<SemanticId> domainTypeIds)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        BoundedContextId = boundedContextId ?? throw new ArgumentNullException(nameof(boundedContextId));
        AggregateRootTypeId = aggregateRootTypeId ?? throw new ArgumentNullException(nameof(aggregateRootTypeId));

        var ids = (domainTypeIds ?? throw new ArgumentNullException(nameof(domainTypeIds))).ToArray();
        if (ids.Length == 0) {
            throw new ArgumentException("Aggregate must contain at least one DomainType id.", nameof(domainTypeIds));
        }

        if (!ids.Contains(AggregateRootTypeId)) {
            throw new ArgumentException("AggregateRootTypeId must be present in DomainTypeIds.", nameof(aggregateRootTypeId));
        }

        DomainTypeIds = ids;
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record Aggregate {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId BoundedContextId { get; }
    public SemanticId AggregateRootTypeId { get; }
    public IReadOnlyList<SemanticId> DomainTypeIds { get; }

    public Aggregate(
        SemanticId semanticId,
        string name,
        SemanticId boundedContextId,
        SemanticId aggregateRootTypeId,
        IEnumerable<SemanticId> domainTypeIds)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Aggregate name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        BoundedContextId = boundedContextId ?? throw new ArgumentNullException(nameof(boundedContextId));
        AggregateRootTypeId = aggregateRootTypeId ?? throw new ArgumentNullException(nameof(aggregateRootTypeId));

        ArgumentNullException.ThrowIfNull(domainTypeIds);
        var typeIds = domainTypeIds.ToArray();
        if (typeIds.Length == 0) {
            throw new ArgumentException("Aggregate must contain at least one DomainTypeId.", nameof(domainTypeIds));
        }

        if (!typeIds.Contains(aggregateRootTypeId)) {
            throw new ArgumentException("AggregateRootTypeId must be present in DomainTypeIds.", nameof(aggregateRootTypeId));
        }

        DomainTypeIds = typeIds;
    }
}

public sealed record AggregateRoot(SemanticId SemanticId, string Name) {
    public AggregateRoot : this
    {
        ArgumentNullException.ThrowIfNull(SemanticId);
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("AggregateRoot name cannot be null, empty, or whitespace.", nameof(Name));
}
    }
}namespace Poly.DomainModeling.V2.Core;

public sealed record Aggregate {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId BoundedContextId { get; }
    public SemanticId AggregateRootTypeId { get; }
    public IReadOnlyList<SemanticId> DomainTypeIds { get; }

    public Aggregate(
        SemanticId semanticId,
        string name,
        SemanticId boundedContextId,
        SemanticId aggregateRootTypeId,
        IEnumerable<SemanticId> domainTypeIds)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Aggregate name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        BoundedContextId = boundedContextId ?? throw new ArgumentNullException(nameof(boundedContextId));
        AggregateRootTypeId = aggregateRootTypeId ?? throw new ArgumentNullException(nameof(aggregateRootTypeId));

        ArgumentNullException.ThrowIfNull(domainTypeIds);
        var typeIds = domainTypeIds.ToArray();
        if (typeIds.Length == 0) {
            throw new ArgumentException("Aggregate must contain at least one DomainType id.", nameof(domainTypeIds));
        }

        if (!typeIds.Contains(aggregateRootTypeId)) {
            throw new ArgumentException("AggregateRootTypeId must be present in DomainTypeIds.", nameof(aggregateRootTypeId));
        }

        DomainTypeIds = typeIds;
    }
}

public sealed record AggregateRoot(SemanticId SemanticId, string Name);