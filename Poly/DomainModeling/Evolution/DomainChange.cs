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
        context.UpdateEntity(EntityName, e => e with { Properties = e.Properties.Append(Property).ToList() });
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' ({Property.Type.TypeName}) to Entity '{EntityName}'";
}

public sealed record RemovePropertyFromEntityChange(
    string EntityName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Properties = e.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateEntity(EntityName, e => e with {
            Stages = e.Stages.Append(new Stage(Name, Parent, [], [], [], [])).ToList()
        });
    }

    internal override string GetDescription() => $"Add Stage '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveStageChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Stages = e.Stages.Where(s => !string.Equals(s.Name, Name, StringComparison.Ordinal)).ToList()
        });
    }

    internal override string GetDescription() => $"RemoveStage({EntityName}.{Name})";
}

public sealed record AddActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Actions = e.Actions.Append(new Action(Name, new InvocationResult([]), [], [], [])).ToList()
        });
    }

    internal override string GetDescription() => $"Add Action '{Name}' to Entity '{EntityName}'";
}

public sealed record RemoveActionChange(
    string EntityName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Actions = e.Actions.Where(a => !string.Equals(a.Name, Name, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateAction(EntityName, ActionName, a => a with {
            Effects = a.Effects.Append(Effect).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Add effect to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddPolicyToEntityChange(
    string EntityName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with { Policies = e.Policies.Append(Policy).ToList() });
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
        context.UpdateStage(EntityName, StageName, s => s with { Policies = s.Policies.Append(Policy).ToList() });
    }

    internal override string GetDescription() => $"Add Policy '{Policy.Name}' to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record AddPolicyToActionChange(
    string EntityName,
    string ActionName,
    Policy Policy
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateAction(EntityName, ActionName, a => a with {
            Policies = a.Policies.Append(Policy).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Add Policy '{Policy.Name}' to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemovePolicyFromEntityChange(
    string EntityName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Policies = e.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateStage(EntityName, StageName, s => s with {
            Policies = s.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
        });
    }

    internal override string GetDescription() => $"RemovePolicyFromStage({EntityName}.{StageName}.{PolicyName})";
}

public sealed record RemovePolicyFromActionChange(
    string EntityName,
    string ActionName,
    string PolicyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateAction(EntityName, ActionName, a => a with {
            Policies = a.Policies.Where(p => !string.Equals(p.Name, PolicyName, StringComparison.Ordinal)).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Remove Policy '{PolicyName}' from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddParameterToActionChange(
    string EntityName,
    string ActionName,
    Property Parameter
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateAction(EntityName, ActionName, a => a with {
            Parameters = a.Parameters.Append(Parameter).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Add parameter '{Parameter.Name}' to Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record AddOnEntryEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateStage(EntityName, StageName, s => s with {
            OnEntryEffects = s.OnEntryEffects.Append(Effect).ToList()
        });
    }

    internal override string GetDescription() => $"Add OnEntry effect to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record AddOnExitEffectToStageChange(
    string EntityName,
    string StageName,
    Effect Effect
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateStage(EntityName, StageName, s => s with {
            OnExitEffects = s.OnExitEffects.Append(Effect).ToList()
        });
    }

    internal override string GetDescription() => $"AddOnExitEffectToStage({EntityName}.{StageName})";
}

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
        context.UpdateAction(EntityName, ActionName, a => a with {
            Parameters = a.Parameters.Where(p => !string.Equals(p.Name, ParameterName, StringComparison.Ordinal)).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Remove parameter '{ParameterName}' from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemoveEffectFromActionChange(
    string EntityName,
    string ActionName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateAction(EntityName, ActionName, a => a with {
            Effects = a.Effects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
        }, searchStages: true);
    }

    internal override string GetDescription() => $"Remove effect from Action '{ActionName}' on Entity '{EntityName}'";
}

public sealed record RemoveOnEntryEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateStage(EntityName, StageName, s => s with {
            OnEntryEffects = s.OnEntryEffects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
        });
    }

    internal override string GetDescription() => $"RemoveOnEntryEffectFromStage({EntityName}.{StageName})";
}

public sealed record RemoveOnExitEffectFromStageChange(
    string EntityName,
    string StageName,
    Effect EffectToRemove
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateStage(EntityName, StageName, s => s with {
            OnExitEffects = s.OnExitEffects.Where(eff => !ReferenceEquals(eff, EffectToRemove)).ToList()
        });
    }

    internal override string GetDescription() => $"RemoveOnExitEffectFromStage({EntityName}.{StageName})";
}

public sealed record SetActionResultChange(
    string EntityName,
    string ActionName,
    InvocationResult Result
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateAction(EntityName, ActionName, a => a with { Result = Result }, searchStages: true);
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
        context.UpdateEntity(EntityName, e => e with { Events = e.Events.Append(EventReference).ToList() });
    }

    internal override string GetDescription() => $"AddEventReferenceToEntity({EntityName}.{EventReference.TypeName})";
}

public sealed record RemoveEventReferenceFromEntityChange(
    string EntityName,
    string EventName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateEntity(EntityName, e => e with {
            Events = e.Events.Where(er => !string.Equals(er.TypeName, EventName, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateStage(EntityName, StageName, s => s with {
            Actions = s.Actions.Append(new Action(Name, new InvocationResult([]), [], [], [])).ToList()
        });
    }

    internal override string GetDescription() => $"Add Action '{Name}' to Stage '{StageName}' on Entity '{EntityName}'";
}

public sealed record RemoveActionFromStageChange(
    string EntityName,
    string StageName,
    string Name
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateStage(EntityName, StageName, s => s with {
            Actions = s.Actions.Where(a => !string.Equals(a.Name, Name, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateRelationship(RelationshipName, r => r with {
            Properties = r.Properties.Append(Property).ToList()
        });
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' to Relationship '{RelationshipName}'";
}

public sealed record RemovePropertyFromRelationshipChange(
    string RelationshipName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateRelationship(RelationshipName, r => r with {
            Properties = r.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
        });
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
        context.UpdateProperty(EntityName, PropertyName, p => p with {
            Constraints = p.Constraints.Append(Constraint).ToList()
        });
    }

    internal override string GetDescription() => $"Add constraint {Constraint.GetType().Name} to property '{PropertyName}' on Entity '{EntityName}'";
}

public sealed record RemoveConstraintFromPropertyChange(
    string EntityName,
    string PropertyName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateProperty(EntityName, PropertyName, p => p with {
            Constraints = p.Constraints.Where(c => !ReferenceEquals(c, Constraint)).ToList()
        });
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
        context.UpdateType(TypeName, t => t with { Constraints = t.Constraints.Append(Constraint).ToList() });
    }

    internal override string GetDescription() => $"Add constraint {Constraint.GetType().Name} to type '{TypeName}'";
}

public sealed record RemoveConstraintFromDomainTypeChange(
    string TypeName,
    Constraint Constraint
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateType(TypeName, t => t with {
            Constraints = t.Constraints.Where(c => !ReferenceEquals(c, Constraint)).ToList()
        });
    }

    internal override string GetDescription() => $"Remove constraint from type '{TypeName}'";
}

public sealed record AddPropertyToEventChange(
    string EventName,
    Property Property
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateType(EventName, t => t with { Properties = t.Properties.Append(Property).ToList() });
    }

    internal override string GetDescription() => $"Add property '{Property.Name}' to Event '{EventName}'";
}

public sealed record RemovePropertyFromEventChange(
    string EventName,
    string PropertyName
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateType(EventName, t => t with {
            Properties = t.Properties.Where(p => !string.Equals(p.Name, PropertyName, StringComparison.Ordinal)).ToList()
        });
    }

    internal override string GetDescription() => $"Remove property '{PropertyName}' from Event '{EventName}'";
}

public sealed record ChangePropertyTypeChange(
    string EntityName,
    string PropertyName,
    DomainTypeReference NewType
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateProperty(EntityName, PropertyName, p => p with { Type = NewType });
    }

    internal override string GetDescription() => $"Change property '{PropertyName}' type on Entity '{EntityName}'";
}

public sealed record SetRelationshipShapeChange(
    string RelationshipName,
    DomainTypeReference? NewSource = null,
    DomainTypeReference? NewTarget = null,
    RelationshipCardinality? NewCardinality = null
) : DomainChange {
    internal override void ApplyTo(DomainMutationContext context) {
        context.UpdateRelationship(RelationshipName, r => r with {
            Source = NewSource ?? r.Source,
            Target = NewTarget ?? r.Target,
            Cardinality = NewCardinality ?? r.Cardinality
        });
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
        context.UpdateType(TypeName, t => t is PrimitiveType pt
            ? pt with { TypeCategory = NewCategory }
            : t);
    }

    internal override string GetDescription() => $"Set primitive type '{TypeName}' category to {NewCategory}";
}