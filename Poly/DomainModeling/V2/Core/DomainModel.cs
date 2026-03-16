namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainModel is an envelope. Full graph traversal requires an IModelGraphRegistry (see T70).
/// </summary>
public sealed record DomainModel {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public ModelVersion ModelVersion { get; }
    public RuleSetVersion RuleSetVersion { get; }
    public SemanticId? LifecycleModelId { get; }
    public IReadOnlyList<SemanticId> BoundedContextIds { get; }

    public DomainModel(
        SemanticId semanticId,
        string name,
        ModelVersion modelVersion,
        RuleSetVersion ruleSetVersion,
        SemanticId? lifecycleModelId,
        IEnumerable<SemanticId>? boundedContextIds = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        ModelVersion = modelVersion;
        RuleSetVersion = ruleSetVersion;
        LifecycleModelId = lifecycleModelId;
        BoundedContextIds = (boundedContextIds ?? Array.Empty<SemanticId>()).ToArray();
    }
}
namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainModel is an envelope. Full graph traversal requires an IModelGraphRegistry (see T70).
/// </summary>
public sealed record DomainModel {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public ModelVersion ModelVersion { get; }
    public RuleSetVersion RuleSetVersion { get; }
    public SemanticId? LifecycleModelId { get; }
    public IReadOnlyList<SemanticId> BoundedContextIds { get; }

    public DomainModel(
        SemanticId semanticId,
        string name,
        ModelVersion modelVersion,
        RuleSetVersion ruleSetVersion,
        IEnumerable<SemanticId> boundedContextIds,
        SemanticId? lifecycleModelId = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainModel name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        ModelVersion = modelVersion;
        RuleSetVersion = ruleSetVersion;
        LifecycleModelId = lifecycleModelId;

        ArgumentNullException.ThrowIfNull(boundedContextIds);
        BoundedContextIds = boundedContextIds.ToArray();
    }
}
namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainModel is an envelope. Full graph traversal requires an IModelGraphRegistry (see T70).
/// </summary>
public sealed record DomainModel {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public ModelVersion ModelVersion { get; }
    public RuleSetVersion RuleSetVersion { get; }
    public SemanticId? LifecycleModelId { get; }
    public IReadOnlyList<SemanticId> BoundedContextIds { get; }

    public DomainModel(
        SemanticId semanticId,
        string name,
        ModelVersion modelVersion,
        RuleSetVersion ruleSetVersion,
        IEnumerable<SemanticId> boundedContextIds,
        SemanticId? lifecycleModelId = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainModel name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        ModelVersion = modelVersion;
        RuleSetVersion = ruleSetVersion;
        LifecycleModelId = lifecycleModelId;
        ArgumentNullException.ThrowIfNull(boundedContextIds);
        BoundedContextIds = boundedContextIds.ToArray();
    }
}
namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainModel is an envelope. Full graph traversal requires an IModelGraphRegistry (see T70).
/// </summary>
public sealed record DomainModel {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public ModelVersion ModelVersion { get; }
    public RuleSetVersion RuleSetVersion { get; }
    public SemanticId? LifecycleModelId { get; }
    public IReadOnlyList<SemanticId> BoundedContextIds { get; }

    public DomainModel(
        SemanticId semanticId,
        string name,
        ModelVersion modelVersion,
        RuleSetVersion ruleSetVersion,
        IEnumerable<SemanticId> boundedContextIds,
        SemanticId? lifecycleModelId = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainModel name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        ModelVersion = modelVersion;
        RuleSetVersion = ruleSetVersion;
        LifecycleModelId = lifecycleModelId;

        ArgumentNullException.ThrowIfNull(boundedContextIds);
        BoundedContextIds = boundedContextIds.ToArray();
    }
}