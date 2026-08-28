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
    /// Resolves <paramref name="actionName"/> on <paramref name="entity"/>: entity-level
    /// first, then any stage action of that name (same dispatch as invoke / bind).
    /// </summary>
    public static Action? FindAction(Entity entity, string actionName) {
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