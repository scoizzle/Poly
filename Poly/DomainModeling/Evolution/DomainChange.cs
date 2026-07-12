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
        context.ModifiedNodes.Add(newEntity);
    }

    internal override string GetDescription() => $"Add Entity '{Name}'";
}

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
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with { Properties = e.Properties.Append(Property).ToList() }),
            $"Entity '{EntityName}' not found — cannot add property '{Property.Name}'");
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' ({Property.Type.TypeName}) to Entity '{EntityName}'";
}

public sealed record RemovePropertyFromEntityChange(
    string EntityName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Properties = e.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove property '{PropertyName}'");
    }

    internal override string GetDescription() => $"RemoveProperty({EntityName}.{PropertyName})";
}

/// <summary>
/// Adds a new Stage to an Entity (MVP: no parent, no initial actions/policies/effects).
/// </summary>
public sealed record AddStageChange(
    string EntityName,
    string Name,
    StageReference? Parent = null
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Stages = e.Stages.Append(new Stage(Name, Parent, [], [], [], [])).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot add stage '{Name}'");
    }

    internal override string GetDescription() => $"Add Stage '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveStageChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Stages = e.Stages.Where(s => !string.Equals(s.Name, Name, StringComparison.Ordinal)).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove stage '{Name}'");
    }

    internal override string GetDescription() => $"RemoveStage({EntityName}.{Name})";
}

public sealed record AddActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Actions = e.Actions.Append(new Action(Name, InvocationResult.Void, [], [], [])).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot add action '{Name}'");
    }

    internal override string GetDescription() => $"Add Action '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Actions = e.Actions.Where(a => !string.Equals(a.Name, Name, StringComparison.Ordinal)).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove action '{Name}'");
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
            context.UpdateAction(EntityName, ActionName, a => a with {
                Effects = a.Effects.Append(Effect).ToList()
            }, searchStages: true),
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
            context.UpdateEntity(EntityName, e => e with { Policies = e.Policies.Append(Policy).ToList() }),
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
            context.UpdateStage(EntityName, StageName, s => s with { Policies = s.Policies.Append(Policy).ToList() }),
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
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Policies = e.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove policy '{PolicyName}'");
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
            context.UpdateAction(EntityName, ActionName, a => a with {
                Parameters = a.Parameters.Append(Parameter).ToList()
            }, searchStages: true),
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
            context.UpdateStage(EntityName, StageName, s => s with {
                OnEntryEffects = s.OnEntryEffects.Append(Effect).ToList()
            }),
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
            context.UpdateStage(EntityName, StageName, s => s with {
                OnExitEffects = s.OnExitEffects.Append(Effect).ToList()
            }),
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
        context.Relationships.Add(newRel);
        context.ModifiedNodes.Add(newRel);
    }

    internal override string GetDescription() => $"AddRelationship({Name})";
}

public sealed record RemoveRelationshipChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Relationships.RemoveAll(r => string.Equals(r.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveRelationship({Name})";
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

public sealed record AddEventChange(
    string Name,
    IReadOnlyList<Property> Properties
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        var newEvent = new Event(Name, Properties, []);
        context.Types.Add(newEvent);
        context.ModifiedNodes.Add(newEvent);
    }

    internal override string GetDescription() => $"AddEvent({Name})";
}

public sealed record RemoveEventChange(
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.Types.RemoveAll(t => t is Event ev && string.Equals(ev.Name, Name, StringComparison.Ordinal));
    }

    internal override string GetDescription() => $"RemoveEvent({Name})";
}

public sealed record AddEventReferenceToEntityChange(
    string EntityName,
    DomainTypeReference EventReference
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with { Events = e.Events.Append(EventReference).ToList() }),
            $"Entity '{EntityName}' not found — cannot add event reference '{EventReference.TypeName}'");
    }

    internal override string GetDescription() => $"AddEventReferenceToEntity({EntityName}.{EventReference.TypeName})";
}

public sealed record RemoveEventReferenceFromEntityChange(
    string EntityName,
    string EventName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                Events = e.Events.Where(er => !string.Equals(er.TypeName, EventName, StringComparison.Ordinal)).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove event reference '{EventName}'");
    }

    internal override string GetDescription() => $"RemoveEventReferenceFromEntity({EntityName}.{EventName})";
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
        context.RequireUpdate(
            context.UpdateStage(EntityName, StageName, s => s with {
                Actions = s.Actions.Append(new Action(Name, InvocationResult.Void, [], [], [])).ToList()
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
/// Adds a Property to a Relationship.
/// </summary>
public sealed record AddPropertyToRelationshipChange(
    string RelationshipName,
    Property Property
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(RelationshipName, r => r with {
                Properties = r.Properties.Append(Property).ToList()
            }),
            $"Relationship '{RelationshipName}' not found — cannot add property '{Property.Name}'");
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' to Relationship '{RelationshipName}'";
}

public sealed record RemovePropertyFromRelationshipChange(
    string RelationshipName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(RelationshipName, r => r with {
                Properties = r.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
            }),
            $"Relationship '{RelationshipName}' not found — cannot remove property '{PropertyName}'");
    }

    internal override string GetDescription() => $"Remove property '{PropertyName}' from Relationship '{RelationshipName}'";
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
/// Adds a Constraint to a DomainType (Entity, ValueType, Event, PrimitiveType).
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

public sealed record AddPropertyToEventChange(
    string EventName,
    Property Property
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(EventName, t => t with { Properties = t.Properties.Append(Property).ToList() }),
            $"Event '{EventName}' not found — cannot add property '{Property.Name}'");
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' to Event '{EventName}'";
}

public sealed record RemovePropertyFromEventChange(
    string EventName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateType(EventName, t => t with {
                Properties = t.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
            }),
            $"Event '{EventName}' not found — cannot remove property '{PropertyName}'");
    }

    internal override string GetDescription() => $"Remove property '{PropertyName}' from Event '{EventName}'";
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
    string RelationshipName,
    DomainTypeReference? NewSource = null,
    DomainTypeReference? NewTarget = null,
    RelationshipCardinality? NewCardinality = null,
    bool? NewSourceOwnsTarget = null
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(RelationshipName, r => r with {
                Source = NewSource ?? r.Source,
                Target = NewTarget ?? r.Target,
                Cardinality = NewCardinality ?? r.Cardinality,
                SourceOwnsTarget = NewSourceOwnsTarget ?? r.SourceOwnsTarget
            }),
            $"Relationship '{RelationshipName}' not found — cannot update shape");
    }

    internal override string GetDescription() => $"Update relationship shape for '{RelationshipName}'";
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

public sealed record AddEventSubscriptionChange(
    string EntityName,
    EventSubscription Subscription
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Append(Subscription).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot add event subscription");
    }

    internal override string GetDescription() =>
        $"Add event subscription '{Subscription.HandlerActionName}<-{Subscription.EventType.TypeName}' to '{EntityName}'";
}

public sealed record RemoveEventSubscriptionChange(
    string EntityName,
    string EventTypeName,
    string HandlerActionName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Where(s =>
                    !(string.Equals(s.EventType.TypeName, EventTypeName, StringComparison.Ordinal)
                      && string.Equals(s.HandlerActionName, HandlerActionName, StringComparison.Ordinal))
                ).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove event subscription");
    }

    internal override string GetDescription() =>
        $"Remove event subscription '{HandlerActionName}<-{EventTypeName}' from '{EntityName}'";
}

public sealed record AddEventSubscriptionCorrelationChange(
    string EntityName,
    string EventTypeName,
    string HandlerActionName,
    EventCorrelationBinding Binding
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Select(s =>
                    string.Equals(s.EventType.TypeName, EventTypeName, StringComparison.Ordinal)
                    && string.Equals(s.HandlerActionName, HandlerActionName, StringComparison.Ordinal)
                        ? s with { Correlations = s.Correlations.Append(Binding).ToList() }
                        : s
                ).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot add correlation binding");
    }

    internal override string GetDescription() =>
        $"Add correlation binding '{Binding.EventPropertyName}->{Binding.ConsumerPropertyName}' to subscription '{HandlerActionName}<-{EventTypeName}' on '{EntityName}'";
}

public sealed record RemoveEventSubscriptionCorrelationChange(
    string EntityName,
    string EventTypeName,
    string HandlerActionName,
    string EventPropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Select(s =>
                    string.Equals(s.EventType.TypeName, EventTypeName, StringComparison.Ordinal)
                    && string.Equals(s.HandlerActionName, HandlerActionName, StringComparison.Ordinal)
                        ? s with {
                            Correlations = s.Correlations.Where(b =>
                            !string.Equals(b.EventPropertyName, EventPropertyName, StringComparison.Ordinal)
                        ).ToList()
                        }
                        : s
                ).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot remove correlation binding");
    }

    internal override string GetDescription() =>
        $"Remove correlation binding for event property '{EventPropertyName}' from subscription '{HandlerActionName}<-{EventTypeName}' on '{EntityName}'";
}

public sealed record SetEventSubscriptionRoutingModeChange(
    string EntityName,
    string EventTypeName,
    string HandlerActionName,
    EventSubscriptionRoutingMode RoutingMode
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Select(s =>
                    string.Equals(s.EventType.TypeName, EventTypeName, StringComparison.Ordinal)
                    && string.Equals(s.HandlerActionName, HandlerActionName, StringComparison.Ordinal)
                        ? s with { RoutingMode = RoutingMode }
                        : s
                ).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot set routing mode");
    }

    internal override string GetDescription() =>
        $"Set routing mode to {RoutingMode} for subscription '{HandlerActionName}<-{EventTypeName}' on '{EntityName}'";
}

public sealed record SetEventSubscriptionEventParameterChange(
    string EntityName,
    string EventTypeName,
    string HandlerActionName,
    string EventParameterName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with {
                EventSubscriptions = e.EventSubscriptions.Select(s =>
                    string.Equals(s.EventType.TypeName, EventTypeName, StringComparison.Ordinal)
                    && string.Equals(s.HandlerActionName, HandlerActionName, StringComparison.Ordinal)
                        ? s with { EventParameterName = EventParameterName }
                        : s
                ).ToList()
            }),
            $"Entity '{EntityName}' not found — cannot set event parameter name");
    }

    internal override string GetDescription() =>
        $"Set event parameter name to '{EventParameterName}' for subscription '{HandlerActionName}<-{EventTypeName}' on '{EntityName}'";
}

public sealed record AddStageToRelationshipChange(
    string RelationshipName,
    Stage Stage
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(RelationshipName, r => r with {
                Stages = r.Stages.Append(Stage).ToList()
            }),
            $"Relationship '{RelationshipName}' not found — cannot add stage '{Stage.Name}'");
    }

    internal override string GetDescription() => $"Add stage '{Stage.Name}' to relationship '{RelationshipName}'";
}

public sealed record RemoveStageFromRelationshipChange(
    string RelationshipName,
    string StageName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateRelationship(RelationshipName, r => r with {
                Stages = r.Stages.Where(s => !string.Equals(s.Name, StageName, StringComparison.Ordinal)).ToList()
            }),
            $"Relationship '{RelationshipName}' not found — cannot remove stage '{StageName}'");
    }

    internal override string GetDescription() => $"Remove stage '{StageName}' from relationship '{RelationshipName}'";
}

public sealed record AddPolicyToRelationshipChange(
    string RelationshipName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.AddPolicyToRelationship(RelationshipName, Policy),
            $"Relationship '{RelationshipName}' not found — cannot add policy '{Policy.Name}'");
    }

    internal override string GetDescription() => $"Add policy '{Policy.Name}' to relationship '{RelationshipName}'";
}

public sealed record RemovePolicyFromRelationshipChange(
    string RelationshipName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.RemovePolicyFromRelationship(RelationshipName, PolicyName),
            $"Relationship '{RelationshipName}' not found — cannot remove policy '{PolicyName}'");
    }

    internal override string GetDescription() => $"Remove policy '{PolicyName}' from relationship '{RelationshipName}'";
}

public sealed record SetEntityParentChange(
    string EntityName,
    string? ParentEntityName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.RequireUpdate(
            context.UpdateEntity(EntityName, e => e with { ParentEntityName = ParentEntityName }),
            $"Entity '{EntityName}' not found — cannot set parent");
    }

    internal override string GetDescription() =>
        ParentEntityName is not null
            ? $"Set parent of '{EntityName}' to '{ParentEntityName}'"
            : $"Clear parent of '{EntityName}'";
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