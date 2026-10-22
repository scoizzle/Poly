using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Shared helper for operations commonly needed across domain model analyzers.
/// </summary>
internal static class EffectHelpers {
    /// <summary>
    /// True when any stage's entry or exit (including nested in <c>if</c>) holds a
    /// transition. Such entities carry the automatic-transition loop guard
    /// (<c>NoteAutomaticStage</c> / <c>ClearAutomaticStageChain</c>) on both sides.
    /// </summary>
    public static bool HasAutomaticTransitions(Entity entity) =>
        entity.Stages.Any(s =>
            FlattenEffects(s.OnEntryEffects).Any(e => e is StageTransitionEffect)
            || FlattenEffects(s.OnExitEffects).Any(e => e is StageTransitionEffect));

    /// <summary>
    /// True when an action, entry, exit, or subscription on <paramref name="entity"/>
    /// creates into <paramref name="relationshipName"/>. Nested <c>if</c> effects count.
    /// This is what makes lowering emit the per-nav <c>Create{Nav}</c> factory.
    /// </summary>
    public static bool CreatesIn(Entity entity, string relationshipName) {
        static bool Match(IEnumerable<Effect> effects, string name) =>
            FlattenEffects(effects).Any(e => e is CreateEntityInRelationshipEffect create
                && string.Equals(create.RelationshipName, name, StringComparison.Ordinal));

        if (entity.Actions.Any(a => Match(a.Effects, relationshipName)))
            return true;
        if (entity.Subscriptions.Any(s => Match(s.Effects, relationshipName)))
            return true;
        foreach (var stage in entity.Stages) {
            if (Match(stage.OnEntryEffects, relationshipName)
                || Match(stage.OnExitEffects, relationshipName))
                return true;
            if (stage.Actions.Any(a => Match(a.Effects, relationshipName)))
                return true;
            if (stage.Subscriptions.Any(s => Match(s.Effects, relationshipName)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Flattens a list of effects, recursively expanding <see cref="CompositeEffect"/>
    /// and <see cref="ConditionalEffect"/> into a depth-first sequence.
    /// The top-level effects themselves are included (in order), and nested children
    /// follow immediately after their parent in the sequence.
    /// </summary>
    public static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            switch (effect) {
                case ConditionalEffect ce:
                    foreach (var nested in FlattenEffects(ce.ThenEffects)) {
                        yield return nested;
                    }
                    if (ce.ElseEffects is not null) {
                        foreach (var nested in FlattenEffects(ce.ElseEffects)) {
                            yield return nested;
                        }
                    }
                    break;
                case CompositeEffect ce:
                    foreach (var nested in FlattenEffects(ce.Effects)) {
                        yield return nested;
                    }
                    break;
            }
        }
    }
}