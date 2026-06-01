using Poly.DomainModeling;

namespace Poly.DomainModeling.Evolution;

internal sealed class DomainMutationContext {
    public string DomainName { get; set; }

    public List<DomainType> Types { get; }

    public List<Relationship> Relationships { get; }

    /// <summary>
    /// Nodes that were modified during mutation (populated by Update* helpers and direct additions).
    /// Used by DomainEvolution.GetAffectedNodes instead of a post-hoc switch over DomainChange subtypes.
    /// </summary>
    public List<Node> ModifiedNodes { get; } = new();

    public DomainMutationContext(Domain source) {
        DomainName = source.Name;
        Types = new List<DomainType>(source.Types);
        Relationships = new List<Relationship>(source.Relationships);
    }

    public Domain ToDomain() => new Domain(DomainName, Types, Relationships);

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
                }
                else if (searchStages) {
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
                }
                return true;
            }
        }
        return false;
    }

    public bool UpdateStage(string entityName, string stageName, Func<Stage, Stage> transform) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, entityName, StringComparison.Ordinal)) {
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

    public Entity? FindEntity(string name) {
        for (int i = 0; i < Types.Count; i++) {
            if (Types[i] is Entity e && string.Equals(e.Name, name, StringComparison.Ordinal))
                return e;
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
}