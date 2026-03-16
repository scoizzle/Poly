namespace Poly.DomainModeling.V2.Core;

public sealed record ParameterDefinition {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string TypeExpression { get; }
    public bool IsOptional { get; }

    public ParameterDefinition(SemanticId semanticId, string name, string typeExpression, bool isOptional = false)
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
        IsOptional = isOptional;
    }
}

public sealed record Command {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId TargetTypeId { get; }
    public string? InitiatedBy { get; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; }
    public string? PreconditionExpression { get; }

    public Command(
        SemanticId semanticId,
        string name,
        SemanticId targetTypeId,
        IEnumerable<ParameterDefinition> parameters,
        string? initiatedBy = null,
        string? preconditionExpression = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));
        InitiatedBy = initiatedBy;
        Parameters = (parameters ?? throw new ArgumentNullException(nameof(parameters))).ToArray();
        PreconditionExpression = preconditionExpression;
    }
}

public enum EffectKind {
    Set,
    Clear,
    Append,
    Remove,
}

/// <summary>
/// Cross-property lifecycle invariant is enforced by the model-graph validator, not at Mutation construction time.
/// </summary>
public sealed record Mutation {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceCommandId { get; }
    public SemanticId TargetTypeId { get; }
    public IReadOnlyList<PropertyEffect> PropertyEffects { get; }

    public Mutation(
        SemanticId semanticId,
        string name,
        SemanticId sourceCommandId,
        SemanticId targetTypeId,
        IEnumerable<PropertyEffect> propertyEffects)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        SourceCommandId = sourceCommandId ?? throw new ArgumentNullException(nameof(sourceCommandId));
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));
        PropertyEffects = (propertyEffects ?? throw new ArgumentNullException(nameof(propertyEffects))).ToArray();
    }
}

public sealed record PropertyEffect(SemanticId PropertyId, EffectKind EffectKind) {
    public SemanticId PropertyId { get; } = PropertyId ?? throw new ArgumentNullException(nameof(PropertyId));
    public EffectKind EffectKind { get; } = EffectKind;
}

public sealed record DomainEvent {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceMutationId { get; }
    public IReadOnlyList<ParameterDefinition> Payload { get; }

    public DomainEvent(
        SemanticId semanticId,
        string name,
        SemanticId sourceMutationId,
        IEnumerable<ParameterDefinition> payload)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));
        }

        Name = name;
        SourceMutationId = sourceMutationId ?? throw new ArgumentNullException(nameof(sourceMutationId));
        Payload = (payload ?? throw new ArgumentNullException(nameof(payload))).ToArray();
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record ParameterDefinition {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string TypeExpression { get; }
    public bool IsOptional { get; }

    public ParameterDefinition(SemanticId semanticId, string name, string typeExpression, bool isOptional = false)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Parameter name cannot be null, empty, or whitespace.", nameof(name))
            : name;

        if (!TypeExpression.TryParse(typeExpression, out _, out _)) {
            throw new ArgumentException("Parameter TypeExpression is invalid for v1 vocabulary.", nameof(typeExpression));
        }

        TypeExpression = typeExpression;
        IsOptional = isOptional;
    }
}

public sealed record Command {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId TargetTypeId { get; }
    public string? InitiatedBy { get; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; }
    public string? PreconditionExpression { get; }

    public Command(
        SemanticId semanticId,
        string name,
        SemanticId targetTypeId,
        IEnumerable<ParameterDefinition> parameters,
        string? initiatedBy = null,
        string? preconditionExpression = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Command name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));
        InitiatedBy = initiatedBy;
        PreconditionExpression = preconditionExpression;

        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = parameters.ToArray();
    }
}

public enum EffectKind {
    Set,
    Clear,
    Append,
    Remove,
}

public sealed record PropertyEffect(SemanticId PropertyId, EffectKind EffectKind);

/// <summary>
/// Cross-property lifecycle invariant is enforced by the model-graph validator, not at Mutation construction time.
/// </summary>
public sealed record Mutation {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceCommandId { get; }
    public SemanticId TargetTypeId { get; }
    public IReadOnlyList<PropertyEffect> PropertyEffects { get; }

    public Mutation(
        SemanticId semanticId,
        string name,
        SemanticId sourceCommandId,
        SemanticId targetTypeId,
        IEnumerable<PropertyEffect> propertyEffects)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Mutation name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        SourceCommandId = sourceCommandId ?? throw new ArgumentNullException(nameof(sourceCommandId));
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));

        ArgumentNullException.ThrowIfNull(propertyEffects);
        PropertyEffects = propertyEffects.ToArray();
    }
}

public sealed record DomainEvent {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceMutationId { get; }
    public IReadOnlyList<ParameterDefinition> Payload { get; }

    public DomainEvent(
        SemanticId semanticId,
        string name,
        SemanticId sourceMutationId,
        IEnumerable<ParameterDefinition> payload)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainEvent name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        SourceMutationId = sourceMutationId ?? throw new ArgumentNullException(nameof(sourceMutationId));

        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload.ToArray();
    }
}
namespace Poly.DomainModeling.V2.Core;

public sealed record ParameterDefinition {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public string TypeExpression { get; }
    public bool IsOptional { get; }

    public ParameterDefinition(SemanticId semanticId, string name, string typeExpression, bool isOptional = false)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("ParameterDefinition name cannot be null, empty, or whitespace.", nameof(name))
            : name;

        if (!TypeExpression.TryParse(typeExpression, out _, out _)) {
            throw new ArgumentException("TypeExpression is invalid for v1 vocabulary.", nameof(typeExpression));
        }

        TypeExpression = typeExpression;
        IsOptional = isOptional;
    }
}

public sealed record Command {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId TargetTypeId { get; }
    public string? InitiatedBy { get; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; }
    public string? PreconditionExpression { get; }

    public Command(
        SemanticId semanticId,
        string name,
        SemanticId targetTypeId,
        IEnumerable<ParameterDefinition> parameters,
        string? initiatedBy = null,
        string? preconditionExpression = null)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Command name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = parameters.ToArray();
        InitiatedBy = initiatedBy;
        PreconditionExpression = preconditionExpression;
    }
}

/// <summary>
/// Cross-property lifecycle invariant is enforced by the model-graph validator, not at Mutation construction time.
/// </summary>
public sealed record Mutation {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceCommandId { get; }
    public SemanticId TargetTypeId { get; }
    public IReadOnlyList<PropertyEffect> PropertyEffects { get; }

    public Mutation(
        SemanticId semanticId,
        string name,
        SemanticId sourceCommandId,
        SemanticId targetTypeId,
        IEnumerable<PropertyEffect> propertyEffects)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Mutation name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        SourceCommandId = sourceCommandId ?? throw new ArgumentNullException(nameof(sourceCommandId));
        TargetTypeId = targetTypeId ?? throw new ArgumentNullException(nameof(targetTypeId));
        ArgumentNullException.ThrowIfNull(propertyEffects);
        PropertyEffects = propertyEffects.ToArray();
    }
}

public sealed record DomainEvent {
    public SemanticId SemanticId { get; }
    public string Name { get; }
    public SemanticId SourceMutationId { get; }
    public IReadOnlyList<ParameterDefinition> Payload { get; }

    public DomainEvent(
        SemanticId semanticId,
        string name,
        SemanticId sourceMutationId,
        IEnumerable<ParameterDefinition> payload)
    {
        SemanticId = semanticId ?? throw new ArgumentNullException(nameof(semanticId));
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("DomainEvent name cannot be null, empty, or whitespace.", nameof(name))
            : name;
        SourceMutationId = sourceMutationId ?? throw new ArgumentNullException(nameof(sourceMutationId));
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload.ToArray();
    }
}

public sealed record PropertyEffect(SemanticId PropertyId, EffectKind EffectKind) {
    public PropertyEffect : this
    {
        ArgumentNullException.ThrowIfNull(PropertyId);
    }
}

public enum EffectKind {
    Set,
    Clear,
    Append,
    Remove,
}