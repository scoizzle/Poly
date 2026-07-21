namespace Poly.DomainModeling.Lowering;

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

    /// <summary>Entity name.</summary>
    public string Name { get; }

    /// <summary>All actions (entity-level + stage-scoped).</summary>
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
        IReadOnlyList<string> effectivePolicies,
        IReadOnlyList<StageTransitionTarget> stageTransitions) {
        EntityName = entityName;
        StageName = stageName;
        Name = name;
        Parameters = parameters;
        IsVoid = isVoid;
        ResultTypeName = resultTypeName;
        EffectivePolicies = effectivePolicies;
        StageTransitions = stageTransitions;
    }

    /// <summary>The entity this action belongs to.</summary>
    public string EntityName { get; }

    /// <summary>Non-null when scoped to a specific lifecycle stage.</summary>
    public string? StageName { get; }

    /// <summary>Action name (PascalCase in the domain).</summary>
    public string Name { get; }

    /// <summary>Action parameters with domain type and entity-ref classification.</summary>
    public IReadOnlyList<BehaviorParameter> Parameters { get; }

    /// <summary>True when the action has no return value (void).</summary>
    public bool IsVoid { get; }

    /// <summary>The result type name when non-void (e.g. "Loan").</summary>
    public string? ResultTypeName { get; }

    /// <summary>Names of policies guarding this action (entity+stage+action combined).</summary>
    public IReadOnlyList<string> EffectivePolicies { get; }

    /// <summary>Stage transitions caused by this action (0 or 1 entries typically).</summary>
    public IReadOnlyList<StageTransitionTarget> StageTransitions { get; }
}

/// <summary>Parameter metadata — domain-typed, host-agnostic.</summary>
public sealed record BehaviorParameter(
    string Name,
    string DomainType,
    bool IsRequired,
    bool IsEntityRef
);

/// <summary>Target stage for a stage-transition effect.</summary>
public sealed record StageTransitionTarget(string TargetStageName);