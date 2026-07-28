using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Mutable snapshot of a <see cref="Domain"/> used during change application.
/// Collects errors when changes target missing entities, stages, or actions
/// — these are surfaced as structural failures in the evolution result.
/// </summary>
internal sealed class DomainMutationContext {
    private readonly MutationTargetIndexMetadata? _mutationIndex;

    internal enum ResolveStatus {
        Found,
        MissingEntity,
        MissingStage,
        MissingAction,
        AmbiguousStage,
        AmbiguousAction
    }

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

    public DomainMutationContext(Domain source, MutationTargetIndexMetadata? mutationIndex = null) {
        DomainName = source.Name;
        Types = new List<DomainType>(source.Types);
        Relationships = new List<Relationship>(source.Relationships);
        ImportedContracts = new List<ImportedContract>(source.ImportedContracts);
        ContractBindings = new List<ContractBinding>(source.ContractBindings);
        _mutationIndex = mutationIndex;
    }

    public Domain ToDomain() => new Domain(DomainName, Types, Relationships) {
        ImportedContracts = ImportedContracts,
        ContractBindings = ContractBindings
    };

    // --- Generic list helpers for ApplyTo methods ---

    /// <summary>
    /// Finds the first element in <paramref name="list"/> matching <paramref name="match"/>,
    /// applies <paramref name="transform"/>, replaces it in-place, and records it in <see cref="ModifiedNodes"/>.
    /// Returns true if a match was found and transformed.
    /// </summary>
    public bool ReplaceInList<T>(List<T> list, Func<T, bool> match, Func<T, T> transform) where T : Node {
        for (int i = 0; i < list.Count; i++) {
            if (match(list[i])) {
                var result = transform(list[i]);
                list[i] = result;
                ModifiedNodes.Add(result);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds an entity by name, checks <paramref name="hasTarget"/>, applies <paramref name="rebuildEntity"/>,
    /// and replaces the entity in-place. Returns true if the entity was found and the target existed.
    /// </summary>
    public bool ReplaceInEntity(string entityName, Func<Entity, bool> hasTarget, Func<Entity, Entity> rebuildEntity) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)) {
                if (!hasTarget(e)) return false;
                var result = rebuildEntity(e);
                Types[i] = result;
                ModifiedNodes.Add(result);
                return true;
            }
        }
        return false;
    }

    // --- Convenience wrappers (thin, tested) ---

    public bool UpdateEntity(string name, Func<Entity, Entity> transform) =>
        ReplaceInList(Types, t => t is Entity e && string.Equals(e.Name, name, StringComparison.Ordinal), t => transform((Entity)t));

    public bool UpdateType(string name, Func<DomainType, DomainType> transform) =>
        ReplaceInList(Types, t => string.Equals(t.Name, name, StringComparison.Ordinal), t => transform(t));

    public bool UpdateRelationship(string name, Func<Relationship, Relationship> transform) =>
        ReplaceInList(Relationships, r => string.Equals(r.Name, name, StringComparison.Ordinal), r => transform(r));

    public bool UpdateImportedContract(string name, Func<ImportedContract, ImportedContract> transform) =>
        ReplaceInList(ImportedContracts, c => string.Equals(c.Name, name, StringComparison.Ordinal), c => transform(c));

    public bool UpdateContractBinding(string name, Func<ContractBinding, ContractBinding> transform) =>
        ReplaceInList(ContractBindings, b => string.Equals(b.Name, name, StringComparison.Ordinal), b => transform(b));

    public bool UpdateAction(string entityName, string actionName, Func<Action, Action> transform, bool searchStages = false) {
        // Try entity-level actions first
        if (ReplaceInEntity(entityName,
                e => e.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)),
                e => e with {
                    Actions = e.Actions.Select(a =>
                    string.Equals(a.Name, actionName, StringComparison.Ordinal) ? transform(a) : a).ToList()
                }))
            return true;

        if (!searchStages) return false;

        // Fall back to stage-level actions
        return ReplaceInEntity(entityName,
            e => e.Stages.Any(s => s.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))),
            e => e with {
                Stages = e.Stages.Select(s => s.Actions.Any(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal))
                    ? s with {
                        Actions = s.Actions.Select(a =>
                        string.Equals(a.Name, actionName, StringComparison.Ordinal) ? transform(a) : a).ToList()
                    }
                    : s).ToList()
            });
    }

    public bool UpdateStage(string entityName, string stageName, Func<Stage, Stage> transform) {
        var status = ResolveStage(entityName, stageName, out _);
        if (status is ResolveStatus.MissingEntity or ResolveStatus.MissingStage or ResolveStatus.AmbiguousStage)
            return false;

        return ReplaceInEntity(entityName,
            e => e.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)),
            e => e with {
                Stages = e.Stages.Select(s =>
                string.Equals(s.Name, stageName, StringComparison.Ordinal) ? transform(s) : s).ToList()
            });
    }

    public bool UpdateProperty(string entityName, string propertyName, Func<Property, Property> transform) =>
        ReplaceInEntity(entityName,
            e => e.Properties.Any(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal)),
            e => e with {
                Properties = e.Properties.Select(p =>
                string.Equals(p.Name, propertyName, StringComparison.Ordinal) ? transform(p) : p).ToList()
            });

    public bool UpdateRelationshipStage(string relationshipName, string stageName, Func<Stage, Stage> transform) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal));
        if (idx < 0) return false;
        var r = Relationships[idx];
        if (!r.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)))
            return false;
        Relationships[idx] = r with {
            Stages = r.Stages.Select(s =>
            string.Equals(s.Name, stageName, StringComparison.Ordinal) ? transform(s) : s).ToList()
        };
        ModifiedNodes.Add(Relationships[idx]);
        return true;
    }

    public bool AddPolicyToRelationship(string name, Policy policy) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (idx < 0) return false;
        var r = Relationships[idx];
        Relationships[idx] = r with { Policies = r.Policies.Append(policy).ToList() };
        ModifiedNodes.Add(Relationships[idx]);
        return true;
    }

    public bool RemovePolicyFromRelationship(string name, string policyName) {
        var idx = Relationships.FindIndex(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        if (idx < 0) return false;
        var r = Relationships[idx];
        Relationships[idx] = r with { Policies = r.Policies.Where(p => !string.Equals(p.Name, policyName, StringComparison.Ordinal)).ToList() };
        ModifiedNodes.Add(Relationships[idx]);
        return true;
    }

    public void AddType(DomainType type) {
        Types.Add(type);
        ModifiedNodes.Add(type);
    }

    public Entity? FindEntity(string name) =>
        (Entity?)Types.Find(t => t is Entity e && string.Equals(e.Name, name, StringComparison.Ordinal));

    public Relationship? FindRelationship(string name) =>
        Relationships.Find(r => string.Equals(r.Name, name, StringComparison.Ordinal));

    public DomainType? FindType(string name) =>
        Types.Find(t => string.Equals(t.Name, name, StringComparison.Ordinal));

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

    public ResolveStatus ResolveStage(string entityName, string stageName, out Stage? stage) {
        stage = null;
        if (_mutationIndex is not null) {
            if (_mutationIndex.EntitiesByName.ContainsKey(entityName)
                && _mutationIndex.StagesByEntity.TryGetValue(entityName, out var stagesByName)
                && stagesByName.TryGetValue(stageName, out var resolvedStage)) {
                stage = resolvedStage;
                return ResolveStatus.Found;
            }

            // DM-META-REMOVE-FALLBACK: allow newly-added stages in the same
            // mutation batch to resolve from the live context.
            var liveEntity = FindEntity(entityName);
            if (liveEntity is null)
                return ResolveStatus.MissingEntity;

            var liveMatches = liveEntity.Stages
                .Where(s => string.Equals(s.Name, stageName, StringComparison.Ordinal))
                .ToList();

            if (liveMatches.Count == 0)
                return ResolveStatus.MissingStage;
            if (liveMatches.Count > 1)
                return ResolveStatus.AmbiguousStage;

            stage = liveMatches[0];
            return ResolveStatus.Found;
        }

        // DM-META-REMOVE-FALLBACK: remove direct stage scan once mutation target
        // index metadata is required for all evolution mutation contexts.
        var entity = FindEntity(entityName);
        if (entity is null)
            return ResolveStatus.MissingEntity;

        var matches = entity.Stages
            .Where(s => string.Equals(s.Name, stageName, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
            return ResolveStatus.MissingStage;
        if (matches.Count > 1)
            return ResolveStatus.AmbiguousStage;

        stage = matches[0];
        return ResolveStatus.Found;
    }

    public ResolveStatus ResolveAction(
        string entityName,
        string actionName,
        bool searchStages,
        out Action? action) {
        action = null;
        if (_mutationIndex is not null) {
            if (_mutationIndex.EntitiesByName.ContainsKey(entityName)
                && _mutationIndex.ActionsByEntity.TryGetValue(entityName, out var actionsByName)
                && actionsByName.TryGetValue(actionName, out var matches)
                && matches.Count > 0) {
                if (!searchStages) {
                    var entityAction = matches.FirstOrDefault(candidate =>
                        _mutationIndex.EntitiesByName[entityName].Actions.Any(a => ReferenceEquals(a, candidate)));
                    if (entityAction is null)
                        return ResolveStatus.MissingAction;
                    action = entityAction;
                    return ResolveStatus.Found;
                }

                if (matches.Count > 1)
                    return ResolveStatus.AmbiguousAction;

                action = matches[0];
                return ResolveStatus.Found;
            }

            // DM-META-REMOVE-FALLBACK: allow newly-added actions in the same
            // mutation batch to resolve from the live context.
        }

        // DM-META-REMOVE-FALLBACK: remove direct action scan once mutation target
        // index metadata is required for all evolution mutation contexts.
        var entity = FindEntity(entityName);
        if (entity is null)
            return ResolveStatus.MissingEntity;

        List<Action> actionMatches =
            entity.Actions.Where(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)).ToList();

        if (searchStages) {
            actionMatches.AddRange(entity.Stages
                .SelectMany(s => s.Actions)
                .Where(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)));
        }

        if (actionMatches.Count == 0)
            return ResolveStatus.MissingAction;
        if (actionMatches.Count > 1)
            return ResolveStatus.AmbiguousAction;

        action = actionMatches[0];
        return ResolveStatus.Found;
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