namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainType.Name uniqueness within its BoundedContext is enforced by the model-graph builder, not at construction time.
/// </summary>
public sealed record DomainType {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId BoundedContextId { get; }
    public SemanticId? LifecycleModelId { get; }
    public IReadOnlyList<DomainProperty> Properties { get; }

    public DomainType(
        SemanticId semanticId,
        string name,
        SemanticId boundedContextId,
        IEnumerable<DomainProperty> properties,
        SemanticId? lifecycleModelId = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        BoundedContextId = boundedContextId ?? throw new ArgumentNullException(nameof(boundedContextId));
        LifecycleModelId = lifecycleModelId;

        var resolvedProperties = (properties ?? throw new ArgumentNullException(nameof(properties))).ToArray();
        var duplicatePropertyNames = resolvedProperties
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicatePropertyNames.Length > 0) {
            throw new ArgumentException($"Duplicate property names are not allowed: {string.Join(", ", duplicatePropertyNames)}", nameof(properties));
        }

        foreach (var property in resolvedProperties) {
            if (property.IsDerivedFromLifecycle && !property.IsReadOnly) {
                throw new ArgumentException(
                    $"Property '{property.Name}' is derived from lifecycle and must be read-only.",
                    nameof(properties));
            }

            if (lifecycleModelId is not null && IsLifecycleStatusPropertyName(property.Name)) {
                if (!property.IsDerivedFromLifecycle || !property.IsReadOnly) {
                    throw new ArgumentException(
                        $"Property '{property.Name}' must be lifecycle-derived and read-only when LifecycleModelId is set.",
                        nameof(properties));
                }
            }
        }

        Properties = resolvedProperties;
    }

    private static bool IsLifecycleStatusPropertyName(string name)
        => string.Equals(name, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "state", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "phase", StringComparison.OrdinalIgnoreCase);
}

public sealed record DomainProperty {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string TypeExpression { get; }
    public bool IsRequired { get; }
    public bool IsReadOnly { get; }
    public bool IsDerivedFromLifecycle { get; }

    public DomainProperty(
        SemanticId semanticId,
        string name,
        string typeExpression,
        bool isRequired,
        bool isReadOnly = false,
        bool isDerivedFromLifecycle = false)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        if (!TypeExpression.TryParse(typeExpression, out _, out _)) {
            throw new ArgumentException("TypeExpression must match the canonical TypeExpression vocabulary.", nameof(typeExpression));
        }

        Name = name;
        TypeExpression = typeExpression;
        IsRequired = isRequired;
        IsReadOnly = isReadOnly;
        IsDerivedFromLifecycle = isDerivedFromLifecycle;
    }
}
namespace Poly.DomainModeling.V2.Core;

/// <summary>
/// DomainType.Name uniqueness within its BoundedContext is enforced by the model-graph builder, not at construction time.
/// </summary>
public sealed record DomainType {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId BoundedContextId { get; }
    public IReadOnlyList<DomainProperty> Properties { get; }
    public SemanticId? LifecycleModelId { get; }

    public DomainType(
        SemanticId semanticId,
        string name,
        SemanticId boundedContextId,
        IEnumerable<DomainProperty> properties,
        SemanticId? lifecycleModelId = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainType name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        BoundedContextId = boundedContextId ?? throw new ArgumentNullException(nameof(boundedContextId));
        LifecycleModelId = lifecycleModelId;

        ArgumentNullException.ThrowIfNull(properties);
        var propertyList = properties.ToArray();
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in propertyList) {
            ArgumentNullException.ThrowIfNull(property);

            if (!uniqueNames.Add(property.Name)) {
                throw new ArgumentException($"Duplicate property name '{property.Name}' in DomainType '{name}'.", nameof(properties));
            }

            if (property.IsDerivedFromLifecycle && !property.IsReadOnly) {
                throw new ArgumentException(
                    $"Property '{property.Name}' is derived from lifecycle and must be read-only.",
                    nameof(properties));
            }

            if (lifecycleModelId is not null && IsLifecycleControlledName(property.Name)) {
                if (!property.IsDerivedFromLifecycle || !property.IsReadOnly) {
                    throw new ArgumentException(
                        $"Property '{property.Name}' must be lifecycle-derived and read-only when LifecycleModelId is set.",
                        nameof(properties));
                }
            }
        }

        Properties = propertyList;
    }

    private static bool IsLifecycleControlledName(string propertyName) =>
        propertyName.Equals("status", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("state", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("phase", StringComparison.OrdinalIgnoreCase);
}

public sealed record DomainProperty {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string TypeExpression { get; }
    public bool IsRequired { get; }
    public bool IsReadOnly { get; }
    public bool IsDerivedFromLifecycle { get; }

    public DomainProperty(
        SemanticId semanticId,
        string name,
        string typeExpression,
        bool isRequired,
        bool isReadOnly = false,
        bool isDerivedFromLifecycle = false)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainProperty name cannot be null, empty, or whitespace.", nameof(name))
            : name;

        if (!TypeExpression.TryParse(typeExpression, out _, out _)) {
            throw new ArgumentException("TypeExpression is invalid for v1 vocabulary.", nameof(typeExpression));
        }

        if (isDerivedFromLifecycle && !isReadOnly) {
            throw new ArgumentException("Lifecycle-derived properties must be read-only.", nameof(isReadOnly));
        }

        TypeExpression = typeExpression;
        IsRequired = isRequired;
        IsReadOnly = isReadOnly;
        IsDerivedFromLifecycle = isDerivedFromLifecycle;
    }
}