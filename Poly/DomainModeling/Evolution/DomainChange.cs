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

    // ── Static helpers for common ApplyTo patterns ────────────────

    /// <summary>
    /// Adds an item to the context Types collection and marks it as modified.
    /// Shape A: direct add.
    /// </summary>
    internal static void AddToTypes(DomainMutationContext context, DomainType type) {
        context.Types.Add(type);
        context.ModifiedNodes.Add(type);
    }

    /// <summary>
    /// Removes all matching items from a list. Fails via <see cref="DomainMutationContext.RequireTarget"/> if none removed.
    /// Shape B: remove with guard.
    /// </summary>
    internal static void RemoveAllWithGuard<T>(
        DomainMutationContext context, List<T> list, Func<T, bool> match, string notFoundMessage) {
        var removed = list.RemoveAll(t => match(t));
        if (removed == 0)
            context.RequireTarget(false, notFoundMessage);
    }

    /// <summary>
    /// Removes a named child from an entity's collection with an existence pre-check.
    /// Checks if the child exists first; if not, reports target missing.
    /// Otherwise delegates to the update lambda.
    /// </summary>
    internal static void RemoveFromEntity<T>(
        DomainMutationContext context,
        string entityName,
        Func<Entity, IReadOnlyList<T>> getChildren,
        Func<Entity, IReadOnlyList<T>, Entity> rebuild,
        Func<T, bool> match,
        string childTypeLabel,
        string childName) {
        var entity = context.FindEntity(entityName);
        if (entity is not null && !getChildren(entity).Any(match)) {
            context.RequireTarget(false, $"'{childName}' not found on {childTypeLabel} '{entityName}' — nothing to remove");
            return;
        }
        context.RequireUpdate(
            context.ReplaceInEntity(entityName, _ => true, e => rebuild(e, getChildren(e).Where(i => !match(i)).ToList())),
            $"Entity '{entityName}' not found — cannot remove {childTypeLabel} child '{childName}'");
    }

    /// <summary>Adds an effect to an action with a RequireUpdate guard.</summary>
    internal static void UpdateActionWithEffect(
        DomainMutationContext context,
        string entityName, string actionName,
        Func<Action, Action> addEffect, string failMsg) {
        context.RequireUpdate(
            context.UpdateAction(entityName, actionName, addEffect, searchStages: true), failMsg);
    }
}

/// <summary>
/// Adds a new top-level Entity with the given name and initial properties.
/// </summary>
public sealed record AddEntityChange(
    string Name,
    IReadOnlyList<Property> InitialProperties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        AddToTypes(context, new Entity(Name, InitialProperties, [], [], []));
    }

    internal override string GetDescription() => $"Add Entity '{Name}'";
}

public sealed record RemoveEntityChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        RemoveAllWithGuard(context, context.Types,
            t => t is Entity e && string.Equals(e.Name, Name, StringComparison.Ordinal),
            $"Entity '{Name}' not found — nothing to remove");
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
        context.RequireUpdate(
            context.AppendChildToEntity(EntityName, e => e.Properties, (e, props) => e with { Properties = props }, Property),
            $"Entity '{EntityName}' not found — cannot add property '{Property.Name}'");
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' ({Property.Type.TypeName}) to Entity '{EntityName}'";
}

public sealed record RemovePropertyFromEntityChange(
    string EntityName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        RemoveFromEntity(context, EntityName,
            e => e.Properties,
            (e, props) => e with { Properties = props },
            p => string.Equals(p.Name, PropertyName, StringComparison.Ordinal),
            "Entity", PropertyName);
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
        context.RequireUpdate(
            context.AppendChildToEntity(EntityName, e => e.Stages,
                (e, stages) => e with { Stages = stages }, new Stage(Name, [], [], [], [])),
            $"Entity '{EntityName}' not found — cannot add stage '{Name}'");
    }

    internal override string GetDescription() => $"Add Stage '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveStageChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        RemoveFromEntity(context, EntityName,
            e => e.Stages,
            (e, stages) => e with { Stages = stages },
            s => string.Equals(s.Name, Name, StringComparison.Ordinal),
            "Entity", Name);
    }

    internal override string GetDescription() => $"RemoveStage({EntityName}.{Name})";
}

public sealed record AddActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToEntity(EntityName, e => e.Actions,
                (e, actions) => e with { Actions = actions }, new Action(Name, InvocationResult.Void, [], [], [])),
            $"Entity '{EntityName}' not found — cannot add action '{Name}'");
    }

    internal override string GetDescription() => $"Add Action '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        RemoveFromEntity(context, EntityName,
            e => e.Actions,
            (e, actions) => e with { Actions = actions },
            a => string.Equals(a.Name, Name, StringComparison.Ordinal),
            "Entity", Name);
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
        context.RequireUpdate(
            context.AppendChildToAction(EntityName, ActionName, a => a.Effects,
                (a, effects) => a with { Effects = effects }, Effect, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot add effect");
    }

    internal override string GetDescription() => $"Add effect to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddPolicyToEntityChange(
    string EntityName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToEntity(EntityName, e => e.Policies,
                (e, policies) => e with { Policies = policies }, Policy),
            $"Entity '{EntityName}' not found — cannot add policy '{Policy.Name}'");
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
        context.RequireUpdate(
            context.AppendChildToStage(EntityName, StageName, s => s.Policies,
                (s, policies) => s with { Policies = policies }, Policy),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot add policy '{Policy.Name}'");
    }

    internal override string GetDescription() => $"Add Policy '{Policy.Name}' to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record AddPolicyToActionChange(
    string EntityName,
    string ActionName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var actionStatus = context.ResolveAction(EntityName, ActionName, searchStages: true, out _);
        if (actionStatus == DomainMutationContext.ResolveStatus.AmbiguousAction) {
            context.Errors.Add(
                $"Action '{ActionName}' on Entity '{EntityName}' is ambiguous — cannot add policy '{Policy.Name}'.");
            return;
        }

        context.RequireUpdate(
            context.UpdateAction(EntityName, ActionName, a => a with {
                Policies = a.Policies.Append(Policy).ToList()
            }, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot add policy '{Policy.Name}'");
    }

    internal override string GetDescription() => $"Add Policy '{Policy.Name}' to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemovePolicyFromEntityChange(
    string EntityName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        RemoveFromEntity(context, EntityName,
            e => e.Policies,
            (e, policies) => e with { Policies = policies },
            p => string.Equals(p.Name, PolicyName, StringComparison.Ordinal),
            "Entity", PolicyName);
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
        var stageStatus = context.ResolveStage(EntityName, StageName, out var stage);
        if (stageStatus == DomainMutationContext.ResolveStatus.AmbiguousStage) {
            context.Errors.Add(
                $"Stage '{StageName}' on Entity '{EntityName}' is ambiguous — cannot remove policy '{PolicyName}'.");
            return;
        }

        if (stageStatus == DomainMutationContext.ResolveStatus.Found
            && stage is not null
            && !stage.Policies.Any(p => string.Equals(p.Name, PolicyName, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Policy '{PolicyName}' not found on Stage '{StageName}' of Entity '{EntityName}' — nothing to remove");
            return;
        }

        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                Policies = s.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot remove policy '{PolicyName}'");
    }

    internal override string GetDescription() => $"RemovePolicyFromStage({EntityName}.{StageName}.{PolicyName})";
}

public sealed record RemovePolicyFromActionChange(
    string EntityName,
    string ActionName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var actionStatus = context.ResolveAction(EntityName, ActionName, searchStages: true, out var action);
        if (actionStatus == DomainMutationContext.ResolveStatus.AmbiguousAction) {
            context.Errors.Add(
                $"Action '{ActionName}' on Entity '{EntityName}' is ambiguous — cannot remove policy '{PolicyName}'.");
            return;
        }

        if (actionStatus == DomainMutationContext.ResolveStatus.Found
            && action is not null
            && !action.Policies.Any(p => string.Equals(p.Name, PolicyName, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Policy '{PolicyName}' not found on Action '{ActionName}' of Entity '{EntityName}' — nothing to remove");
            return;
        }

        context.RequireUpdate(
            context.UpdateAction(EntityName, ActionName, a => a with {
                Policies = a.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
            }, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot remove policy '{PolicyName}'");
    }

    internal override string GetDescription() => $"Remove Policy '{PolicyName}' from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddParameterToActionChange(
    string EntityName,
    string ActionName,
    Property Parameter
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToAction(EntityName, ActionName, a => a.Parameters,
                (a, parameters) => a with { Parameters = parameters }, Parameter, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot add parameter '{Parameter.Name}'");
    }

    internal override string GetDescription() => $"Add parameter '{Parameter.Name}' to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddOnEntryEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToStage(EntityName, StageName, s => s.OnEntryEffects,
                (s, effects) => s with { OnEntryEffects = effects }, Effect),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot add OnEntry effect");
    }

    internal override string GetDescription() => $"Add OnEntry effect to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record AddOnExitEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToStage(EntityName, StageName, s => s.OnExitEffects,
                (s, effects) => s with { OnExitEffects = effects }, Effect),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot add OnExit effect");
    }

    internal override string GetDescription() => $"AddOnExitEffectToStage({EntityName}.{StageName})";
}

public sealed record AddRelationshipChange(
    string Name,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties,
    bool SourceOwnsTarget = false
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newRel = new Relationship(Name, Source, Target, Cardinality, Properties) {
            SourceOwnsTarget = SourceOwnsTarget
        };
        var added = context.ReplaceInEntity(Source.TypeName,
            _ => true,
            e => e with { Navigations = [.. e.Navigations, newRel] });
        if (!added)
            context.RequireTarget(false, $"Relationship '{Name}' cannot be added — source entity '{Source.TypeName}' not found");
        context.ModifiedNodes.Add(newRel);
    }

    internal override string GetDescription() => $"AddRelationship({Name})";
}

public sealed record RemoveRelationshipChange(
    string SourceEntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var removed = context.ReplaceInEntity(SourceEntityName,
            e => e.Navigations.Any(r => string.Equals(r.Name, Name, StringComparison.Ordinal)),
            e => e with {
                Navigations = e.Navigations.Where(r => !string.Equals(r.Name, Name, StringComparison.Ordinal)).ToList()
            });
        if (!removed)
            context.RequireTarget(false, $"Relationship '{Name}' on entity '{SourceEntityName}' not found — nothing to remove");
    }

    internal override string GetDescription() => $"RemoveRelationship({SourceEntityName}.{Name})";
}

public sealed record RemoveParameterFromActionChange(
    string EntityName,
    string ActionName,
    string ParameterName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateAction(EntityName, ActionName, a => a with {
                Parameters = a.Parameters.Where(p => !string.Equals(p.Name, ParameterName, StringComparison.Ordinal)).ToList()
            }, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot remove parameter '{ParameterName}'");
    }

    internal override string GetDescription() => $"Remove parameter '{ParameterName}' from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemoveEffectFromActionChange(
    string EntityName,
    string ActionName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateAction(EntityName, ActionName, a => a with {
                Effects = a.Effects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
            }, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot remove effect");
    }

    internal override string GetDescription() => $"Remove effect from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemoveOnEntryEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                OnEntryEffects = s.OnEntryEffects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot remove OnEntry effect");
    }

    internal override string GetDescription() => $"RemoveOnEntryEffectFromStage({EntityName}.{StageName})";
}

public sealed record RemoveOnExitEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                OnExitEffects = s.OnExitEffects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot remove OnExit effect");
    }

    internal override string GetDescription() => $"RemoveOnExitEffectFromStage({EntityName}.{StageName})";
}

public sealed record SetActionResultChange(
    string EntityName,
    string ActionName,
    InvocationResult Result
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateAction(EntityName, ActionName, a => a with { Result = Result }, searchStages: true),
            $"Action '{ActionName}' on Entity '{EntityName}' not found — cannot set result");
    }

    internal override string GetDescription() => $"Set result for Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddValueTypeChange(
    string Name,
    IReadOnlyList<Property> Properties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newValueType = new ValueType(Name, Properties, []);
        context.Types.Add(newValueType);
        context.ModifiedNodes.Add(newValueType);
    }

    internal override string GetDescription() => $"AddValueType({Name})";
}

public sealed record RemoveValueTypeChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is ValueType v && string.Equals(v.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveValueType({Name})";
}

/// <summary>
/// Adds a PrimitiveType to the domain.
/// </summary>
public sealed record AddPrimitiveTypeChange(
    string Name,
    TypeCategory TypeCategory,
    IReadOnlyList<Constraint> Constraints
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newPrimitive = new PrimitiveType(Name, TypeCategory, Constraints);
        context.Types.Add(newPrimitive);
        context.ModifiedNodes.Add(newPrimitive);
    }

    internal override string GetDescription() => $"Add PrimitiveType '{Name}' ({TypeCategory})";
}

public sealed record RemovePrimitiveTypeChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is PrimitiveType p && string.Equals(p.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"Remove PrimitiveType '{Name}'";
}

public sealed record AddActionToStageChange(
    string EntityName,
    string StageName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        // SA: Look for an entity-level action with the same name. If found,
        // copy its effects, policies, parameters, and result type so the
        // stage-scoped action is not an empty shell. This prevents the silent
        // no-op that occurs when AddActionToStage creates an empty copy while
        // effects were added to the entity-level action only.
        // See Phase 3 §6e (Stage-Action Semantics).
        var actionStatus = context.ResolveAction(EntityName, Name, searchStages: false, out var source);
        if (actionStatus == DomainMutationContext.ResolveStatus.AmbiguousAction) {
            context.Errors.Add(
                $"Action '{Name}' on Entity '{EntityName}' is ambiguous — cannot add action to stage '{StageName}'.");
            return;
        }

        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                Actions = s.Actions.Append(
                    source is not null
                        ? new Action(Name, source.Result, source.Parameters.ToArray(),
                            source.Effects.ToArray(), source.Policies.ToArray())
                        : new Action(Name, InvocationResult.Void, [], [], [])
                ).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot add action '{Name}'");
    }

    internal override string GetDescription() => $"Add Action '{Name}' to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record RemoveActionFromStageChange(
    string EntityName,
    string StageName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var stageStatus = context.ResolveStage(EntityName, StageName, out var stage);
        if (stageStatus == DomainMutationContext.ResolveStatus.AmbiguousStage) {
            context.Errors.Add(
                $"Stage '{StageName}' on Entity '{EntityName}' is ambiguous — cannot remove action '{Name}'.");
            return;
        }

        if (stageStatus == DomainMutationContext.ResolveStatus.Found
            && stage is not null
            && !stage.Actions.Any(a => string.Equals(a.Name, Name, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Action '{Name}' not found on Stage '{StageName}' of Entity '{EntityName}' — nothing to remove");
            return;
        }

        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                Actions = s.Actions.Where(a => !string.Equals(a.Name, Name, StringComparison.Ordinal)).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot remove action '{Name}'");
    }

    internal override string GetDescription() => $"Remove Action '{Name}' from Stage '{StageName}' on Entity '{EntityName}'";
}

/// <summary>
/// Sets the name of the Domain.
/// </summary>
public sealed record SetDomainNameChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.DomainName = Name;
        // Domain root itself is not in ModifiedNodes (the root is replaced by ToDomain)
    }

    internal override string GetDescription() => $"Set domain name to '{Name}'";
}

/// <summary>
/// Adds one extension id to the domain (additive). Duplicate id fails closed.
/// </summary>
public sealed record AddDomainExtensionChange(
    string ExtensionId
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExtensionId);
        if (context.Extensions.Any(id => string.Equals(id, ExtensionId, StringComparison.Ordinal))) {
            context.Errors.Add($"Domain already depends on extension '{ExtensionId}'.");
            return;
        }
        context.Extensions.Add(ExtensionId);
    }

    internal override string GetDescription() => $"Add domain extension '{ExtensionId}'";
}

/// <summary>
/// Adds a Property to a Relationship.
/// </summary>
public sealed record AddPropertyToRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    Property Property
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(SourceEntityName, RelationshipName, r => r with {
                Properties = r.Properties.Append(Property).ToList()
            }),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot add property '{Property.Name}'");
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' to Relationship '{SourceEntityName}.{RelationshipName}'";
}

public sealed record RemovePropertyFromRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var rel = context.FindRelationship(SourceEntityName, RelationshipName);
        if (rel is not null && !rel.Properties.Any(p => string.Equals(p.Name, PropertyName, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Property '{PropertyName}' not found on Relationship '{RelationshipName}' — nothing to remove");
            return;
        }
        context.RequireUpdate(
            context.UpdateRelationship(SourceEntityName, RelationshipName, r => r with {
                Properties = r.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
            }),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot remove property '{PropertyName}'");
    }

    internal override string GetDescription() => $"Remove property '{PropertyName}' from Relationship '{SourceEntityName}.{RelationshipName}'";
}

/// <summary>
/// Adds a Constraint to an Entity's Property by name.
/// </summary>
public sealed record AddConstraintToPropertyChange(
    string EntityName,
    string PropertyName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateProperty(EntityName, PropertyName, p => p with {
                Constraints = p.Constraints.Append(Constraint).ToList()
            }),
            $"Property '{PropertyName}' on Entity '{EntityName}' not found — cannot add constraint");
    }

    internal override string GetDescription() => $"Add constraint {Constraint.GetType().Name} to property '{PropertyName}' on Entity '{EntityName}'";
}

public sealed record RemoveConstraintFromPropertyChange(
    string EntityName,
    string PropertyName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateProperty(EntityName, PropertyName, p => p with {
                Constraints = p.Constraints.Where(c => !ReferenceEquals(c, Constraint)).ToList()
            }),
            $"Property '{PropertyName}' on Entity '{EntityName}' not found — cannot remove constraint");
    }

    internal override string GetDescription() => $"Remove constraint from property '{PropertyName}' on Entity '{EntityName}'";
}

/// <summary>
/// Adds a Constraint to a DomainType (Entity, ValueType, PrimitiveType).
/// </summary>
public sealed record AddConstraintToDomainTypeChange(
    string TypeName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(TypeName, t => t with { Constraints = t.Constraints.Append(Constraint).ToList() }),
            $"Type '{TypeName}' not found — cannot add constraint");
    }

    internal override string GetDescription() => $"Add constraint {Constraint.GetType().Name} to type '{TypeName}'";
}

public sealed record RemoveConstraintFromDomainTypeChange(
    string TypeName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(TypeName, t => t with {
                Constraints = t.Constraints.Where(c => !ReferenceEquals(c, Constraint)).ToList()
            }),
            $"Type '{TypeName}' not found — cannot remove constraint");
    }

    internal override string GetDescription() => $"Remove constraint from type '{TypeName}'";
}

/// <summary>
/// Adds a Facet to an Entity's (or other DomainType's) Properties by name.
/// </summary>
public sealed record AddFacetToPropertyChange(
    string EntityName,
    string PropertyName,
    Facet Facet
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateProperty(EntityName, PropertyName, p => p with {
                Facets = p.Facets.Append(Facet).ToList()
            }),
            $"Property '{PropertyName}' on Entity '{EntityName}' not found — cannot add facet");
    }

    internal override string GetDescription() => $"Add facet {Facet.GetType().Name} to property '{PropertyName}' on Entity '{EntityName}'";
}

/// <summary>
/// Adds a Facet to a DomainType (Entity, ValueType, PrimitiveType, EnumType).
/// </summary>
public sealed record AddFacetToDomainTypeChange(
    string TypeName,
    Facet Facet
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(TypeName, t => t with { Facets = t.Facets.Append(Facet).ToList() }),
            $"Type '{TypeName}' not found — cannot add facet");
    }

    internal override string GetDescription() => $"Add facet {Facet.GetType().Name} to type '{TypeName}'";
}

public sealed record ChangePropertyTypeChange(
    string EntityName,
    string PropertyName,
    DomainTypeReference NewType
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateProperty(EntityName, PropertyName, p => p with { Type = NewType }),
            $"Property '{PropertyName}' on Entity '{EntityName}' not found — cannot change type");
    }

    internal override string GetDescription() => $"Change property '{PropertyName}' type on Entity '{EntityName}'";
}

public sealed record SetRelationshipShapeChange(
    string SourceEntityName,
    string RelationshipName,
    DomainTypeReference? NewSource = null,
    DomainTypeReference? NewTarget = null,
    RelationshipCardinality? NewCardinality = null,
    bool? NewSourceOwnsTarget = null
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(SourceEntityName, RelationshipName, r => r with {
                Source = NewSource ?? r.Source,
                Target = NewTarget ?? r.Target,
                Cardinality = NewCardinality ?? r.Cardinality,
                SourceOwnsTarget = NewSourceOwnsTarget ?? r.SourceOwnsTarget
            }),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot update shape");
    }

    internal override string GetDescription() => $"Update relationship shape for '{SourceEntityName}.{RelationshipName}'";
}

/// <summary>
/// Changes the TypeCategory of a PrimitiveType.
/// </summary>
public sealed record SetPrimitiveTypeCategoryChange(
    string TypeName,
    TypeCategory NewCategory
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(TypeName, t => t is PrimitiveType pt
                ? pt with { TypeCategory = NewCategory }
                : t),
            $"Type '{TypeName}' not found — cannot set category");
    }

    internal override string GetDescription() => $"Set primitive type '{TypeName}' category to {NewCategory}";
}

public sealed record AddStageToRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    Stage Stage
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(SourceEntityName, RelationshipName, r => r with {
                Stages = r.Stages.Append(Stage).ToList()
            }),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot add stage '{Stage.Name}'");
    }

    internal override string GetDescription() => $"Add stage '{Stage.Name}' to relationship '{SourceEntityName}.{RelationshipName}'";
}

public sealed record RemoveStageFromRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    string StageName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var rel = context.FindRelationship(SourceEntityName, RelationshipName);
        if (rel is not null && !rel.Stages.Any(s => string.Equals(s.Name, StageName, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Stage '{StageName}' not found on Relationship '{RelationshipName}' — nothing to remove");
            return;
        }
        context.RequireUpdate(
            context.UpdateRelationship(SourceEntityName, RelationshipName, r => r with {
                Stages = r.Stages.Where(s => !string.Equals(s.Name, StageName, StringComparison.Ordinal)).ToList()
            }),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot remove stage '{StageName}'");
    }

    internal override string GetDescription() => $"Remove stage '{StageName}' from relationship '{SourceEntityName}.{RelationshipName}'";
}

public sealed record AddPolicyToRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AddPolicyToRelationship(SourceEntityName, RelationshipName, Policy),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot add policy '{Policy.Name}'");
    }

    internal override string GetDescription() => $"Add policy '{Policy.Name}' to relationship '{SourceEntityName}.{RelationshipName}'";
}

// ── Enum type changes ─────────────────────────────────

public sealed record AddEnumTypeChange(
    string Name,
    IReadOnlyList<string> MemberNames
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.Add(new EnumType(Name, MemberNames, []));
        context.ModifiedNodes.Add(context.Types[^1]);
    }

    internal override string GetDescription() => $"Add enum type '{Name}' with {MemberNames.Count} members";
}

public sealed record RemovePolicyFromRelationshipChange(
    string SourceEntityName,
    string RelationshipName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var rel = context.FindRelationship(SourceEntityName, RelationshipName);
        if (rel is not null && !rel.Policies.Any(p => string.Equals(p.Name, PolicyName, StringComparison.Ordinal))) {
            context.RequireTarget(false,
                $"Policy '{PolicyName}' not found on Relationship '{RelationshipName}' — nothing to remove");
            return;
        }
        context.RequireUpdate(
            context.RemovePolicyFromRelationship(SourceEntityName, RelationshipName, PolicyName),
            $"Relationship '{RelationshipName}' on entity '{SourceEntityName}' not found — cannot remove policy '{PolicyName}'");
    }

    internal override string GetDescription() => $"Remove policy '{PolicyName}' from relationship '{SourceEntityName}.{RelationshipName}'";
}

// --- Contract integration changes ---

public sealed record AddImportedContractChange(
    string Name,
    ContractSourceKind SourceKind,
    string SourceIdentifier,
    string Version
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var contract = new ImportedContract(Name, SourceKind, SourceIdentifier, Version, []);
        context.ImportedContracts.Add(contract);
        context.ModifiedNodes.Add(contract);
    }

    internal override string GetDescription() => $"Add imported contract '{Name}' ({SourceKind}:{SourceIdentifier} v{Version})";
}

public sealed record RemoveImportedContractChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.ImportedContracts.RemoveAll(c => string.Equals(c.Name, Name, StringComparison.Ordinal));
        context.ContractBindings.RemoveAll(b => string.Equals(b.ContractName, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"Remove imported contract '{Name}'";
}

public sealed record AddContractEndpointChange(
    string ContractName,
    ContractEndpoint Endpoint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var idx = context.ImportedContracts.FindIndex(c =>
            string.Equals(c.Name, ContractName, StringComparison.Ordinal));
        if (idx < 0) { context.RequireUpdate(false, $"Contract '{ContractName}' not found — cannot add endpoint"); return; }
        var updated = context.ImportedContracts[idx] with {
            Endpoints = context.ImportedContracts[idx].Endpoints.Append(Endpoint).ToList()
        };
        context.ImportedContracts[idx] = updated;
        context.ModifiedNodes.Add(updated);
    }

    internal override string GetDescription() => $"Add endpoint '{Endpoint.Name}' to contract '{ContractName}'";
}

public sealed record AddContractValueTypeChange(
    string ContractName,
    ValueType ValueType
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var idx = context.ImportedContracts.FindIndex(c =>
            string.Equals(c.Name, ContractName, StringComparison.Ordinal));
        if (idx < 0) {
            context.RequireUpdate(false, $"Contract '{ContractName}' not found — cannot add value type '{ValueType.Name}'");
            return;
        }
        var updated = context.ImportedContracts[idx] with {
            Types = context.ImportedContracts[idx].Types.Append(ValueType).ToList()
        };
        context.ImportedContracts[idx] = updated;
        context.ModifiedNodes.Add(updated);
        context.ModifiedNodes.Add(ValueType);
    }

    internal override string GetDescription() =>
        $"Add value type '{ValueType.Name}' to contract '{ContractName}'";
}

public sealed record RemoveContractEndpointChange(
    string ContractName,
    string EndpointName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var idx = context.ImportedContracts.FindIndex(c =>
            string.Equals(c.Name, ContractName, StringComparison.Ordinal));
        if (idx < 0) return;
        var updated = context.ImportedContracts[idx] with {
            Endpoints = context.ImportedContracts[idx].Endpoints
                .Where(e => !string.Equals(e.Name, EndpointName, StringComparison.Ordinal))
                .ToList()
        };
        context.ImportedContracts[idx] = updated;
        context.ModifiedNodes.Add(updated);
    }

    internal override string GetDescription() => $"Remove endpoint '{EndpointName}' from contract '{ContractName}'";
}

public sealed record AddContractBindingChange(
    string Name,
    string ContractName,
    string EndpointName,
    string ActionName,
    string LocalParameterName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var binding = new ContractBinding(Name, ContractName, EndpointName, ActionName, LocalParameterName, []);
        context.ContractBindings.Add(binding);
        context.ModifiedNodes.Add(binding);
    }

    internal override string GetDescription() => $"Add contract binding '{Name}' ({ContractName}/{EndpointName} -> {ActionName}.{LocalParameterName})";
}

public sealed record RemoveContractBindingChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.ContractBindings.RemoveAll(b =>
            string.Equals(b.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"Remove contract binding '{Name}'";
}

public sealed record AddContractFieldMapChange(
    string BindingName,
    ContractFieldMap FieldMap
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var idx = context.ContractBindings.FindIndex(b =>
            string.Equals(b.Name, BindingName, StringComparison.Ordinal));
        if (idx < 0) return;
        var updated = context.ContractBindings[idx] with {
            FieldMaps = context.ContractBindings[idx].FieldMaps.Append(FieldMap).ToList()
        };
        context.ContractBindings[idx] = updated;
        context.ModifiedNodes.Add(updated);
    }

    internal override string GetDescription() =>
        $"Add field map '{FieldMap.RemoteFieldName}'->'{FieldMap.LocalFieldName}' to binding '{BindingName}'";
}

public sealed record RemoveContractFieldMapChange(
    string BindingName,
    string RemoteFieldName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var idx = context.ContractBindings.FindIndex(b =>
            string.Equals(b.Name, BindingName, StringComparison.Ordinal));
        if (idx < 0) return;
        var updated = context.ContractBindings[idx] with {
            FieldMaps = context.ContractBindings[idx].FieldMaps
                .Where(fm => !string.Equals(fm.RemoteFieldName, RemoteFieldName, StringComparison.Ordinal))
                .ToList()
        };
        context.ContractBindings[idx] = updated;
        context.ModifiedNodes.Add(updated);
    }

    internal override string GetDescription() =>
        $"Remove field map '{RemoteFieldName}' from binding '{BindingName}'";
}

/// <summary>
/// Adds a <see cref="StageSubscription"/> to a stage on an entity.
/// </summary>
public sealed record AddStageSubscriptionChange(
    string EntityName,
    string StageName,
    StageSubscription Subscription
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToStage(EntityName, StageName, s => s.Subscriptions,
                (s, subs) => s with { Subscriptions = subs }, Subscription),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot add stage subscription");
    }

    internal override string GetDescription() =>
        $"Add stage subscription on '{RelationshipName(Subscription)}' to Stage '{StageName}' on Entity '{EntityName}'";

    private static string RelationshipName(StageSubscription sub) =>
        $"{sub.RelationshipName} -> {string.Join("/", sub.StageNames)} ({sub.Quantifier})";
}

/// <summary>
/// Removes a <see cref="StageSubscription"/> from a stage on an entity.
/// Matches by **semantic key** (RelationshipName, StageNames sequence, Quantifier)
/// rather than record identity, because <see cref="StageSubscription"/> inherits
/// <see cref="DomainObject"/> → <see cref="Node"/> so record equality includes <c>Node.Id</c>.
/// If multiple subscriptions match the same semantic key, all are removed.
/// </summary>
public sealed record RemoveStageSubscriptionChange(
    string EntityName,
    string StageName,
    StageSubscription SubscriptionToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var stageStatus = context.ResolveStage(EntityName, StageName, out var stage);
        if (stageStatus == DomainMutationContext.ResolveStatus.AmbiguousStage) {
            context.Errors.Add(
                $"Stage '{StageName}' on Entity '{EntityName}' is ambiguous — cannot remove stage subscription '{SubscriptionKey(SubscriptionToRemove)}'.");
            return;
        }

        if (stageStatus == DomainMutationContext.ResolveStatus.Found
            && stage is not null
            && !stage.Subscriptions.Any(sub => SemanticMatch(sub, SubscriptionToRemove))) {
            context.RequireTarget(false,
                $"Stage subscription with key '{SubscriptionKey(SubscriptionToRemove)}' not found " +
                $"on Stage '{StageName}' of Entity '{EntityName}' — nothing to remove");
            return;
        }

        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                Subscriptions = s.Subscriptions.Where(sub => !SemanticMatch(sub, SubscriptionToRemove)).ToList()
            }),
            $"Stage '{StageName}' on Entity '{EntityName}' not found — cannot remove stage subscription");
    }

    internal override string GetDescription() =>
        $"Remove stage subscription '{SubscriptionKey(SubscriptionToRemove)}' from Stage '{StageName}' on Entity '{EntityName}'";

    private static string SubscriptionKey(StageSubscription sub) =>
        $"{sub.RelationshipName} -> {string.Join("/", sub.StageNames)} ({sub.Quantifier}" +
        (sub.PeerBinding is { Length: > 0 } ? $", as {sub.PeerBinding}" : "") + ")";

    private static bool SemanticMatch(StageSubscription a, StageSubscription b) {
        if (!string.Equals(a.RelationshipName, b.RelationshipName, StringComparison.Ordinal))
            return false;
        if (a.Quantifier != b.Quantifier)
            return false;
        if (!string.Equals(a.PeerBinding, b.PeerBinding, StringComparison.Ordinal))
            return false;
        if (a.StageNames.Count != b.StageNames.Count)
            return false;
        for (int i = 0; i < a.StageNames.Count; i++) {
            if (!string.Equals(a.StageNames[i], b.StageNames[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Adds a <see cref="StageSubscription"/> to an entity (entity-level subscription).
/// Entity-level subscriptions fire regardless of the entity's current stage.
/// </summary>
public sealed record AddEntitySubscriptionChange(
    string EntityName,
    StageSubscription Subscription
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AppendChildToEntity(EntityName, e => e.Subscriptions,
                (e, subs) => e with { Subscriptions = subs }, Subscription),
            $"Entity '{EntityName}' not found — cannot add entity subscription");
    }

    internal override string GetDescription() =>
        $"Add entity subscription '{Subscription.RelationshipName} -> {string.Join("/", Subscription.StageNames)}' to Entity '{EntityName}'";
}