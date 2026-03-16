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

        ArgumentNullException.ThrowIfNull(properties);
        var propertyArray = properties.ToArray();

        var duplicateName = propertyArray
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateName != null) {
            throw new ArgumentException($"Duplicate property name '{duplicateName.Key}' is not allowed in a DomainType.", nameof(properties));
        }

        foreach (var property in propertyArray) {
            if (property.IsDerivedFromLifecycle && !property.IsReadOnly) {
                throw new ArgumentException(
                    $"Property '{property.Name}' is lifecycle-derived and must be read-only.",
                    nameof(properties));
            }
        }

        LifecycleModelId = lifecycleModelId;
        if (LifecycleModelId != null) {
            foreach (var property in propertyArray) {
                if (IsLifecycleStatusName(property.Name) && (!property.IsDerivedFromLifecycle || !property.IsReadOnly)) {
                    throw new ArgumentException(
                        $"Lifecycle status property '{property.Name}' must be lifecycle-derived and read-only when LifecycleModelId is set.",
                        nameof(properties));
                }
            }
        }

        Properties = propertyArray;
    }

    private static bool IsLifecycleStatusName(string name)
        => name.Equals("status", StringComparison.OrdinalIgnoreCase)
           || name.Equals("state", StringComparison.OrdinalIgnoreCase)
           || name.Equals("phase", StringComparison.OrdinalIgnoreCase);
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

        TypeExpression = typeExpression;
        IsRequired = isRequired;
        IsReadOnly = isReadOnly;
        IsDerivedFromLifecycle = isDerivedFromLifecycle;
    }
}