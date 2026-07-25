namespace Poly.DomainModeling.Analysis;

// ═══════════════════════════════════════════════════════════════
// Behavior model — action metadata
//
// A derived domain fact expressing what can be done with an
// entity: action signatures, parameters, return types, effective
// policy guards, and stage transitions.
//
// This is purely derived from the domain model — no protocol or
// storage conventions. Transport codegens consume BehaviorAction
// records and map them to endpoints, mutations, or RPCs.
// Domain types stay platform-agnostic; host-type projection is
// the backend's concern (see DomainTypeMapping for C#).
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Per-entity action metadata — derived facts about the behavior
/// surface of each entity, independent of any protocol.
/// </summary>
public sealed record BehaviorModel(
    string DomainName,
    IReadOnlyList<BehaviorEntity> Entities
);

/// <summary>Behavior-level view of an entity — all actions it exposes.</summary>
public sealed class BehaviorEntity {
    public BehaviorEntity(string name, IReadOnlyList<BehaviorAction> actions) {
        Name = name;
        Actions = actions;
    }

    public string Name { get; }
    public IReadOnlyList<BehaviorAction> Actions { get; }
}

/// <summary>
/// Behavior-level view of an action — parameter shape, return type,
/// effective policy guards, and stage transitions.
/// </summary>
public sealed class BehaviorAction {
    public BehaviorAction(
        string entityName,
        string? stageName,
        string name,
        IReadOnlyList<BehaviorParameter> parameters,
        bool isVoid,
        string? resultTypeName,
        IReadOnlyList<string> policies,
        IReadOnlyList<StageTransitionTarget> transitions) {
        EntityName = entityName;
        StageName = stageName;
        Name = name;
        Parameters = parameters;
        IsVoid = isVoid;
        ResultTypeName = resultTypeName;
        Policies = policies;
        Transitions = transitions;
    }

    public string EntityName { get; }
    public string? StageName { get; }
    public string Name { get; }
    public IReadOnlyList<BehaviorParameter> Parameters { get; }
    public bool IsVoid { get; }
    public string? ResultTypeName { get; }
    public IReadOnlyList<string> Policies { get; }
    public IReadOnlyList<StageTransitionTarget> Transitions { get; }
    /// <summary>Alias for <see cref="Transitions"/> — compatibility.</summary>
    public IReadOnlyList<StageTransitionTarget> StageTransitions => Transitions;
}

/// <summary>Action parameter metadata.</summary>
public sealed class BehaviorParameter {
    public BehaviorParameter(string name, string typeName, bool isRequired, bool isEntityRef) {
        Name = name;
        TypeName = typeName;
        IsRequired = isRequired;
        IsEntityRef = isEntityRef;
    }

    /// <summary>Parameter name.</summary>
    public string Name { get; }
    /// <summary>Domain-level type name (e.g. "Text", "Number", "Book").</summary>
    public string TypeName { get; }
    /// <summary>Alias for <see cref="TypeName"/> — used by codegen.</summary>
    public string DomainType => TypeName;
    /// <summary>Whether this parameter is required.</summary>
    public bool IsRequired { get; }
    /// <summary>Whether this parameter is an entity reference.</summary>
    public bool IsEntityRef { get; }
}

/// <summary>Target stage for a transition effect.</summary>
public sealed record StageTransitionTarget(string Name)
{
    /// <summary>Alias for <see cref="Name"/> — compatibility with existing callers.</summary>
    public string TargetStageName => Name;
}
