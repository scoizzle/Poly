using Poly.DomainModeling;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Base type for changes that can be applied through the evolution layer.
/// 
/// In the MVP we support both a small native DomainChange hierarchy and (where needed)
/// an adapter over the legacy DomainMutationIntent types for MCP compatibility.
/// 
/// Each change is responsible for:
/// - Applying itself to a Domain to produce a new immutable root.
/// - Describing itself for traces.
/// </summary>
public abstract record DomainChange {
    /// <summary>
    /// Applies this change against the given mutable context.
    /// This is the efficient path used for bulk application.
    /// </summary>
    internal abstract void ApplyTo(DomainMutationContext context);

    /// <summary>
    /// Applies this change to a domain and returns a new immutable root.
    /// This is a convenience wrapper that creates a context internally.
    /// Prefer using the batch path when applying multiple changes.
    /// </summary>
    internal Domain ApplyTo(Domain current) {
        var context = new DomainMutationContext(current);
        ApplyTo(context);
        return context.ToDomain();
    }

    /// <summary>
    /// Returns a human-readable description of this change (used for traces).
    /// </summary>
    internal abstract string GetDescription();
}

/// <summary>
/// Adds a new top-level Entity with the given name and initial properties.
/// </summary>
public sealed record AddEntityChange(
    string Name,
    IReadOnlyList<Property> InitialProperties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newEntity = new Entity(Name, InitialProperties, [], [], [], []);
        context.Types.Add(newEntity);
    }

    internal override string GetDescription() => $"AddEntity({Name})";
}

/// <summary>
/// Removes an Entity by name (MVP scope uses name; stable NodeId resolution comes later).
/// </summary>
public sealed record RemoveEntityChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is Entity e && string.Equals(e.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveEntity({Name})";
}

/// <summary>
/// Adds a single property to an existing Entity.
/// </summary>
public sealed record AddPropertyToEntityChange(
    string EntityName,
    Property Property
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newProps = e.Properties.Append(Property).ToList();
                context.Types[i] = e with { Properties = newProps };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddProperty({EntityName}.{Property.Name})";
}

/// <summary>
/// Removes a property from an Entity by name.
/// </summary>
public sealed record RemovePropertyFromEntityChange(
    string EntityName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newProps = e.Properties
                    .Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal))
                    .ToList();
                context.Types[i] = e with { Properties = newProps };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveProperty({EntityName}.{PropertyName})";
}

/// <summary>
/// Adds a new Stage to an Entity (MVP: no parent, no initial actions/policies/effects).
/// </summary>
public sealed record AddStageChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newStage = new Stage(Name, null, [], [], [], []);
                var newStages = e.Stages.Append(newStage).ToList();
                context.Types[i] = e with { Stages = newStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddStage({EntityName}.{Name})";
}

/// <summary>
/// Removes a Stage from an Entity by name.
/// </summary>
public sealed record RemoveStageChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newStages = e.Stages
                    .Where(s => !string.Equals(s.Name, Name, StringComparison.Ordinal))
                    .ToList();
                context.Types[i] = e with { Stages = newStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveStage({EntityName}.{Name})";
}

/// <summary>
/// Adds a minimal Action to an Entity (MVP: empty parameters, effects, policies, result).
/// </summary>
public sealed record AddActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newAction = new Action(Name, new InvocationResult([]), [], [], []);
                var newActions = e.Actions.Append(newAction).ToList();
                context.Types[i] = e with { Actions = newActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddAction({EntityName}.{Name})";
}

/// <summary>
/// Removes an Action from an Entity by name.
/// </summary>
public sealed record RemoveActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newActions = e.Actions
                    .Where(a => !string.Equals(a.Name, Name, StringComparison.Ordinal))
                    .ToList();
                context.Types[i] = e with { Actions = newActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveAction({EntityName}.{Name})";
}

/// <summary>
/// Attaches an Effect (Create, Publish, StageTransition, etc.) to an existing Action on an Entity.
/// </summary>
public sealed record AddEffectToActionChange(
    string EntityName,
    string ActionName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newEffects = a.Effects.Append(Effect).ToList();
                        return a with { Effects = newEffects };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddEffectToAction({EntityName}.{ActionName})";
}

/// <summary>
/// Adds a Policy to an Entity.
/// </summary>
public sealed record AddPolicyToEntityChange(
    string EntityName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newPolicies = e.Policies.Append(Policy).ToList();
                context.Types[i] = e with { Policies = newPolicies };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddPolicyToEntity({EntityName}.{Policy.Name})";
}

/// <summary>
/// Adds a Policy to a Stage on an Entity.
/// </summary>
public sealed record AddPolicyToStageChange(
    string EntityName,
    string StageName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newPolicies = s.Policies.Append(Policy).ToList();
                        return s with { Policies = newPolicies };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddPolicyToStage({EntityName}.{StageName}.{Policy.Name})";
}

/// <summary>
/// Adds a Policy to an Action on an Entity.
/// </summary>
public sealed record AddPolicyToActionChange(
    string EntityName,
    string ActionName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newPolicies = a.Policies.Append(Policy).ToList();
                        return a with { Policies = newPolicies };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddPolicyToAction({EntityName}.{ActionName}.{Policy.Name})";
}

/// <summary>
/// Removes a Policy from an Entity by name.
/// </summary>
public sealed record RemovePolicyFromEntityChange(
    string EntityName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newPolicies = e.Policies
                    .Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal))
                    .ToList();
                context.Types[i] = e with { Policies = newPolicies };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemovePolicyFromEntity({EntityName}.{PolicyName})";
}

/// <summary>
/// Removes a Policy from a Stage on an Entity.
/// </summary>
public sealed record RemovePolicyFromStageChange(
    string EntityName,
    string StageName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newPolicies = s.Policies
                            .Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal))
                            .ToList();
                        return s with { Policies = newPolicies };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemovePolicyFromStage({EntityName}.{StageName}.{PolicyName})";
}

/// <summary>
/// Removes a Policy from an Action on an Entity.
/// </summary>
public sealed record RemovePolicyFromActionChange(
    string EntityName,
    string ActionName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newPolicies = a.Policies
                            .Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal))
                            .ToList();
                        return a with { Policies = newPolicies };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemovePolicyFromAction({EntityName}.{ActionName}.{PolicyName})";
}

/// <summary>
/// Adds a parameter to an Action.
/// </summary>
public sealed record AddParameterToActionChange(
    string EntityName,
    string ActionName,
    Property Parameter
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newParams = a.Parameters.Append(Parameter).ToList();
                        return a with { Parameters = newParams };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddParameterToAction({EntityName}.{ActionName}.{Parameter.Name})";
}

/// <summary>
/// Adds an effect to be executed when entering a Stage.
/// </summary>
public sealed record AddOnEntryEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newEffects = s.OnEntryEffects.Append(Effect).ToList();
                        return s with { OnEntryEffects = newEffects };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddOnEntryEffectToStage({EntityName}.{StageName})";
}

/// <summary>
/// Adds an effect to be executed when exiting a Stage.
/// </summary>
public sealed record AddOnExitEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newEffects = s.OnExitEffects.Append(Effect).ToList();
                        return s with { OnExitEffects = newEffects };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddOnExitEffectToStage({EntityName}.{StageName})";
}

/// <summary>
/// Adds a Relationship between two entities.
/// </summary>
public sealed record AddRelationshipChange(
    string Name,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newRel = new Relationship(Name, Source, Target, Cardinality, Properties);
        context.Relationships.Add(newRel);
    }

    internal override string GetDescription() => $"AddRelationship({Name})";
}

/// <summary>
/// Removes a Relationship by name.
/// </summary>
public sealed record RemoveRelationshipChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Relationships.RemoveAll(r => string.Equals(r.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveRelationship({Name})";
}

/// <summary>
/// Removes a parameter from an Action by name.
/// </summary>
public sealed record RemoveParameterFromActionChange(
    string EntityName,
    string ActionName,
    string ParameterName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newParams = a.Parameters
                            .Where(p => !string.Equals(p.Name, ParameterName, StringComparison.Ordinal))
                            .ToList();
                        return a with { Parameters = newParams };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveParameterFromAction({EntityName}.{ActionName}.{ParameterName})";
}

/// <summary>
/// Removes an effect from an Action (matches by reference or simple equality for MVP).
/// </summary>
public sealed record RemoveEffectFromActionChange(
    string EntityName,
    string ActionName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        var newEffects = a.Effects
                            .Where(eff => !ReferenceEquals(eff, EffectToRemove))
                            .ToList();
                        return a with { Effects = newEffects };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveEffectFromAction({EntityName}.{ActionName})";
}

/// <summary>
/// Removes an OnEntry effect from a Stage.
/// </summary>
public sealed record RemoveOnEntryEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newEffects = s.OnEntryEffects
                            .Where(eff => !ReferenceEquals(eff, EffectToRemove))
                            .ToList();
                        return s with { OnEntryEffects = newEffects };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveOnEntryEffectFromStage({EntityName}.{StageName})";
}

/// <summary>
/// Removes an OnExit effect from a Stage.
/// </summary>
public sealed record RemoveOnExitEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedStages = e.Stages.Select(s => {
                    if (string.Equals(s.Name, StageName, StringComparison.Ordinal)) {
                        var newEffects = s.OnExitEffects
                            .Where(eff => !ReferenceEquals(eff, EffectToRemove))
                            .ToList();
                        return s with { OnExitEffects = newEffects };
                    }
                    return s;
                }).ToList();

                context.Types[i] = e with { Stages = updatedStages };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveOnExitEffectFromStage({EntityName}.{StageName})";
}

/// <summary>
/// Sets (or replaces) the InvocationResult for an Action.
/// </summary>
public sealed record SetActionResultChange(
    string EntityName,
    string ActionName,
    InvocationResult Result
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var updatedActions = e.Actions.Select(a => {
                    if (string.Equals(a.Name, ActionName, StringComparison.Ordinal)) {
                        return a with { Result = Result };
                    }
                    return a;
                }).ToList();

                context.Types[i] = e with { Actions = updatedActions };
                break;
            }
        }
    }

    internal override string GetDescription() => $"SetActionResult({EntityName}.{ActionName})";
}

/// <summary>
/// Adds a ValueType (owned document / composite value) to the domain.
/// </summary>
public sealed record AddValueTypeChange(
    string Name,
    IReadOnlyList<Property> Properties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newValueType = new ValueType(Name, Properties, []);
        context.Types.Add(newValueType);
    }

    internal override string GetDescription() => $"AddValueType({Name})";
}

/// <summary>
/// Removes a ValueType by name.
/// </summary>
public sealed record RemoveValueTypeChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is ValueType v && string.Equals(v.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveValueType({Name})";
}

/// <summary>
/// Adds an Event type to the domain.
/// </summary>
public sealed record AddEventChange(
    string Name,
    IReadOnlyList<Property> Properties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newEvent = new Event(Name, Properties, []);
        context.Types.Add(newEvent);
    }

    internal override string GetDescription() => $"AddEvent({Name})";
}

/// <summary>
/// Removes an Event by name.
/// </summary>
public sealed record RemoveEventChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is Event ev && string.Equals(ev.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveEvent({Name})";
}

/// <summary>
/// Adds a reference to an Event on an Entity.
/// </summary>
public sealed record AddEventReferenceToEntityChange(
    string EntityName,
    DomainTypeReference EventReference
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newEvents = e.Events.Append(EventReference).ToList();
                context.Types[i] = e with { Events = newEvents };
                break;
            }
        }
    }

    internal override string GetDescription() => $"AddEventReferenceToEntity({EntityName}.{EventReference.TypeName})";
}

/// <summary>
/// Removes an Event reference from an Entity by name.
/// </summary>
public sealed record RemoveEventReferenceFromEntityChange(
    string EntityName,
    string EventName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        for (int i = 0; i < context.Types.Count; i++) {
            if (context.Types[i] is Entity e && string.Equals(e.Name, EntityName, StringComparison.Ordinal)) {
                var newEvents = e.Events
                    .Where(er => !string.Equals(er.TypeName, EventName, StringComparison.Ordinal))
                    .ToList();
                context.Types[i] = e with { Events = newEvents };
                break;
            }
        }
    }

    internal override string GetDescription() => $"RemoveEventReferenceFromEntity({EntityName}.{EventName})";
}