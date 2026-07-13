using Poly.DomainModeling;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Mutable snapshot of a <see cref="Domain"/> used during change application.
/// Collects errors when changes target missing entities, stages, or actions
/// — these are surfaced as structural failures in the evolution result.
/// </summary>
internal sealed class DomainMutationContext {
    public string DomainName { get; set; }

    public List<DomainType> Types { get; }

    public List<Relationship> Relationships { get; }

    public List<ImportedContract> ImportedContracts { get; }

    public List<ContractBinding> ContractBindings { get; }

    /// <summary>
    /// Nodes that were modified during mutation (populated by Update* helpers and direct additions).
    /// Used by DomainEvolution.GetAffectedNodes instead of a post-hoc switch over DomainChange subtypes.
    /// </summary>
    public List<Node> ModifiedNodes { get; } = new();

    /// <summary>
    /// Errors collected during change application — e.g. targeting a missing entity, stage, or action.
    /// Checked after all changes are applied; any errors cause structural failure (rollback).
    /// </summary>
    public List<string> Errors { get; } = new();

    public DomainMutationContext(Domain source) {
        DomainName = source.Name;
        Types = new List<DomainType>(source.Types);
        Relationships = new List<Relationship>(source.Relationships);
        ImportedContracts = new List<ImportedContract>(source.ImportedContracts);
        ContractBindings = new List<ContractBinding>(source.ContractBindings);
    }

    public Domain ToDomain() => new Domain(DomainName, Types, Relationships) {
        ImportedContracts = ImportedContracts,
        ContractBindings = ContractBindings
    };

    // --- Resolver helpers for ApplyTo methods ---

    public bool UpdateEntity(string name, Func<Entity, Entity> transform) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, name, StringComparison.Ordinal)) {
                var result = transform(e);
                Types[i] = result;
                ModifiedNodes.Add(result);
                return true;
            }
        }
        return false;
    }

    public bool UpdateType(string name, Func<DomainType, DomainType> transform) {
        for (int i = 0; i < Types.Count; i++) {
            if (string.Equals(Types[i].Name, name, StringComparison.Ordinal)) {
                var result = transform(Types[i]);
                Types[i] = result;
                ModifiedNodes.Add(result);
                return true;
            }
        }
        return false;
    }

    public bool UpdateRelationship(string name, Func<Relationship, Relationship> transform) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (idx >= 0) {
            var result = transform(Relationships[idx]);
            Relationships[idx] = result;
            ModifiedNodes.Add(result);
            return true;
        }
        return false;
    }

    public bool UpdateAction(string entityName, string actionName, Func<Action, Action> transform, bool searchStages = false) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)) {
                var foundAtEntityLevel = e.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));

                if (foundAtEntityLevel) {
                    var updatedActions = e.Actions.Select(a =>
                        string.Equals(a.Name, actionName, StringComparison.Ordinal) ? transform(a) : a
                    ).ToList();
                    var updatedEntity = e with { Actions = updatedActions };
                    Types[i] = updatedEntity;
                    ModifiedNodes.Add(updatedEntity);
                    return true;
                }

                if (searchStages) {
                    var foundInStage = e.Stages.Any(s => s.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)));
                    if (foundInStage) {
                        var updatedStages = e.Stages.Select(s => {
                            if (s.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))) {
                                var stageActions = s.Actions.Select(a =>
                                    string.Equals(a.Name, actionName, StringComparison.Ordinal) ? transform(a) : a
                                ).ToList();
                                return s with { Actions = stageActions };
                            }
                            return s;
                        }).ToList();
                        var updatedEntity = e with { Stages = updatedStages };
                        Types[i] = updatedEntity;
                        ModifiedNodes.Add(updatedEntity);
                        return true;
                    }
                }

                // Entity found but action not found at entity or stage level
                return false;
            }
        }
        return false;
    }

    public bool UpdateStage(string entityName, string stageName, Func<Stage, Stage> transform) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)) {
                // Check that the named stage actually exists
                if (!e.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)))
                    return false;

                var updatedStages = e.Stages.Select(s =>
                    string.Equals(s.Name, stageName, StringComparison.Ordinal) ? transform(s) : s
                ).ToList();
                var updatedEntity = e with { Stages = updatedStages };
                Types[i] = updatedEntity;
                ModifiedNodes.Add(updatedEntity);
                return true;
            }
        }
        return false;
    }

    public bool UpdateProperty(string entityName, string propertyName, Func<Property, Property> transform) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)) {
                // Check that the named property actually exists
                if (!e.Properties.Any(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal)))
                    return false;

                var updatedProps = e.Properties.Select(p =>
                    string.Equals(p.Name, propertyName, StringComparison.Ordinal) ? transform(p) : p
                ).ToList();
                var updatedEntity = e with { Properties = updatedProps };
                Types[i] = updatedEntity;
                ModifiedNodes.Add(updatedEntity);
                return true;
            }
        }
        return false;
    }

    public bool UpdateRelationshipStage(string relationshipName, string stageName, Func<Stage, Stage> transform) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal));
        if (idx >= 0) {
            var r = Relationships[idx];
            // Check that the named stage actually exists
            if (!r.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)))
                return false;

            var updatedStages = r.Stages.Select(s =>
                string.Equals(s.Name, stageName, StringComparison.Ordinal) ? transform(s) : s
            ).ToList();
            Relationships[idx] = r with { Stages = updatedStages };
            ModifiedNodes.Add(Relationships[idx]);
            return true;
        }
        return false;
    }

    public bool AddPolicyToRelationship(string name, Policy policy) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (idx >= 0) {
            var r = Relationships[idx];
            Relationships[idx] = r with { Policies = r.Policies.Append(policy).ToList() };
            ModifiedNodes.Add(Relationships[idx]);
            return true;
        }
        return false;
    }

    public bool RemovePolicyFromRelationship(string name, string policyName) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (idx >= 0) {
            var r = Relationships[idx];
            Relationships[idx] = r with { Policies = r.Policies.Where(p => !string.Equals(p.Name, policyName, StringComparison.Ordinal)).ToList() };
            ModifiedNodes.Add(Relationships[idx]);
            return true;
        }
        return false;
    }

    public Entity? FindEntity(string name) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, name, StringComparison.Ordinal))
                return e;
        }
        return null;
    }

    public Relationship? FindRelationship(string name) {
        foreach (var r in Relationships) {
            if (string.Equals(r.Name, name, StringComparison.Ordinal))
                return r;
        }
        return null;
    }

    public DomainType? FindType(string name) {
        for (int i = 0; i < Types.Count; i++) {
            if (string.Equals(Types[i].Name, name, StringComparison.Ordinal))
                return Types[i];
        }
        return null;
    }

    public bool UpdateImportedContract(string name, Func<ImportedContract, ImportedContract> transform) {
        var idx = ImportedContracts.FindIndex(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (idx >= 0) {
            var result = transform(ImportedContracts[idx]);
            ImportedContracts[idx] = result;
            ModifiedNodes.Add(result);
            return true;
        }
        return false;
    }

    public bool UpdateContractBinding(string name, Func<ContractBinding, ContractBinding> transform) {
        var idx = ContractBindings.FindIndex(b => string.Equals(b.Name, name, StringComparison.Ordinal));
        if (idx >= 0) {
            var result = transform(ContractBindings[idx]);
            ContractBindings[idx] = result;
            ModifiedNodes.Add(result);
            return true;
        }
        return false;
    }

    public Action? FindActionOnAnyEntity(string actionName) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e) {
                var action = e.Actions.FirstOrDefault(a =>
                    string.Equals(a.Name, actionName, StringComparison.Ordinal));
                if (action is not null) return action;
            }
        }
        return null;
    }

    /// <summary>
    /// If <paramref name="updateResult"/> is false, records a descriptive error
    /// message. Called by <c>ApplyTo</c> methods after Update* returns false.
    /// The error causes <see cref="ApplyChanges"/> to produce a structural failure.
    /// </summary>
    public void RequireUpdate(bool updateResult, string failureMessage) {
        if (!updateResult)
            Errors.Add(failureMessage);
    }

    /// <summary>
    /// Records an error if <paramref name="targetExists"/> is false, indicating
    /// a remove-by-name found no matching child on an existing parent.
    /// </summary>
    public void RequireTarget(bool targetExists, string failureMessage) {
        if (!targetExists)
            Errors.Add(failureMessage);
    }
}