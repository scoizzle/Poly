using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Static helpers for common domain-iteration patterns used by analyzers.
/// Eliminates the repetitive "foreach type → is Entity → foreach action/stage" boilerplate.
/// </summary>
internal static class DomainAnalysis {
    /// <summary>
    /// Iterates all entity types in <paramref name="domain"/> and invokes <paramref name="action"/> for each.
    /// </summary>
    public static void ForEachEntity(Domain domain, System.Action<Entity> action) {
        foreach (var type in domain.Types) {
            if (type is Entity entity)
                action(entity);
        }
    }

    /// <summary>
    /// Resolves <paramref name="actionName"/> on <paramref name="entity"/> with the
    /// same dispatch as runtime <c>TryResolveAction</c>.
    /// When <paramref name="currentStage"/> is set: that stage's action (SA empty-copy
    /// fallthrough to entity-level), else entity-level only.
    /// When <paramref name="currentStage"/> is unknown: entity-level only, or the unique
    /// stage body when every matching stage action is equivalent. Differing stage bodies
    /// fail closed (null) — never first-stage-wins.
    /// </summary>
    public static Action? FindAction(Entity entity, string actionName, string? currentStage = null) {
        if (currentStage is not null) {
            var stage = entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, currentStage, StringComparison.Ordinal));
            if (stage is not null) {
                var stageAction = stage.Actions.FirstOrDefault(a =>
                    string.Equals(a.Name, actionName, StringComparison.Ordinal));
                if (stageAction is not null) {
                    // SA: empty stage-copy (no effects/policies) → entity action.
                    if (stageAction.Effects.Count == 0
                        && stageAction.Policies.Count == 0) {
                        var entityOverride = entity.Actions.FirstOrDefault(a =>
                            string.Equals(a.Name, actionName, StringComparison.Ordinal));
                        if (entityOverride is not null)
                            return entityOverride;
                    }
                    return stageAction;
                }
            }
            return entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
        }

        var entityAction = entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, actionName, StringComparison.Ordinal));
        if (entityAction is not null)
            return entityAction;

        Action? unique = null;
        foreach (var stage in entity.Stages) {
            var stageAction = stage.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
            if (stageAction is null) continue;
            if (unique is null) {
                unique = stageAction;
                continue;
            }
            if (unique != stageAction)
                return null;
        }
        return unique;
    }

    /// <summary>
    /// Existence lookup: entity-level first, then any stage action of that name.
    /// Used when a signature check needs a representative and current stage is unknown.
    /// Does not pick a body for effect analysis — use <see cref="FindAction"/>.
    /// </summary>
    public static Action? FindAnyNamedAction(Entity entity, string actionName) {
        var action = entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, actionName, StringComparison.Ordinal));
        if (action is not null) return action;
        foreach (var stage in entity.Stages) {
            action = stage.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal));
            if (action is not null) return action;
        }
        return null;
    }

    /// <summary>The stage that declares <paramref name="action"/>, or null if entity-level.</summary>
    public static string? StageNameOf(Entity entity, Action action) {
        foreach (var stage in entity.Stages) {
            if (stage.Actions.Contains(action))
                return stage.Name;
        }
        return null;
    }

    /// <summary>
    /// First action of <paramref name="actionName"/> on any entity (entity-level or stage).
    /// </summary>
    public static Action? FindActionOnAnyEntity(Domain domain, string actionName) {
        foreach (var type in domain.Types) {
            if (type is not Entity entity) continue;
            var action = FindAction(entity, actionName);
            if (action is not null) return action;
        }
        return null;
    }

    /// <summary>
    /// Iterates all actions on <paramref name="entity"/>, including those on
    /// the entity directly and those on its stages.
    /// </summary>
    public static void ForEachAction(Entity entity, System.Action<Action> action) {
        foreach (var a in entity.Actions)
            action(a);
        foreach (var stage in entity.Stages)
            foreach (var a in stage.Actions)
                action(a);
    }

    /// <summary>
    /// Iterates all actions directly on <paramref name="entity"/> (not stage actions).
    /// </summary>
    public static void ForEachEntityAction(Entity entity, System.Action<Action> action) {
        foreach (var a in entity.Actions)
            action(a);
    }

    /// <summary>
    /// Iterates all stages on <paramref name="entity"/>.
    /// </summary>
    public static void ForEachStage(Entity entity, System.Action<Stage> action) {
        foreach (var stage in entity.Stages)
            action(stage);
    }

    /// <summary>
    /// Iterates all actions on <paramref name="stage"/>.
    /// </summary>
    public static void ForEachStageAction(Stage stage, System.Action<Action> action) {
        foreach (var a in stage.Actions)
            action(a);
    }

    /// <summary>
    /// Iterates all entities, then their actions and stages, invoking the respective callbacks.
    /// </summary>
    public static void ForEachEntityWithActionsAndStages(
        Domain domain,
        System.Action<Entity>? onEntity = null,
        System.Action<Action>? onAction = null,
        System.Action<Stage>? onStage = null) {
        foreach (var type in domain.Types) {
            if (type is not Entity entity) continue;
            onEntity?.Invoke(entity);
            if (onAction is not null)
                foreach (var a in entity.Actions) onAction(a);
            if (onStage is not null)
                foreach (var stage in entity.Stages) {
                    onStage(stage);
                    if (onAction is not null)
                        foreach (var a in stage.Actions) onAction(a);
                }
        }
    }
}