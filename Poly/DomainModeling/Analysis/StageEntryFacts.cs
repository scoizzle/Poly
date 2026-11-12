using System.Collections.Frozen;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The analysis-side answer to "which properties does a new instance's stage-entry chain assign".
/// <para>
/// The walk starts at <see cref="EntityStages.InitialStage"/> and follows the entry chain the
/// exported constructor runs: an <see cref="AssignEffect"/> whose target is a
/// <see cref="PropertyAccess"/> contributes its authored name, and a
/// <see cref="StageTransitionEffect"/> queues its target stage, whose entry effects are walked the
/// same way. Only entry transitions chain — an action's <c>transition</c> does not run at create —
/// and <see cref="Stage.OnExitEffects"/> never contribute. A cycle terminates because each stage is
/// visited once, which yields the same name union as the constructor's entry inlining.
/// </para>
/// <para>
/// Conditional and loop bodies count even when they cannot execute: this is an over-approximation,
/// deliberately, so a create-supplied value for such a property is an error the author must
/// escape with <c>default(x)</c>. Names are returned verbatim, never filtered against
/// <see cref="Entity.Properties"/>, so an assignment to a mistyped name is not silently swallowed;
/// a non-<see cref="PropertyAccess"/> target (a <see cref="RelationshipNavigation"/>) is skipped
/// because it names another entity's property.
/// </para>
/// <para>
/// This is the analysis-side provider. L-09 (or S-14, whichever first consumes it) repoints
/// <see cref="EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames"/> at it and publishes
/// through the existing <see cref="EntityStructureMetadata.EntryAssignedPropertyNames"/> field.
/// No lowering code calls <see cref="StageEntryFacts"/> (<c>docs/CORE.md</c>; <c>RequireStructure</c>).
/// </para>
/// </summary>
internal static class StageEntryFacts {
    /// <summary>
    /// Names of the entity's properties assigned by the entry chain starting at
    /// <see cref="EntityStages.InitialStage"/>. Empty, never null, for an entity with no stages.
    /// </summary>
    public static IReadOnlySet<string> EntryAssignedProperties(Entity entity, Domain domain) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (entity.InitialStage() is not { } initial)
            return FrozenSet<string>.Empty;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Stage>();
        pending.Enqueue(initial);
        while (pending.Count > 0) {
            var stage = pending.Dequeue();
            if (!visited.Add(stage.Name))
                continue;
            foreach (var effect in EffectHelpers.FlattenEffects(stage.OnEntryEffects)) {
                switch (effect) {
                    case AssignEffect { Target: PropertyAccess property }:
                        names.Add(property.Name);
                        break;
                    case StageTransitionEffect transition when FindStage(entity, transition.TargetStage.StageName) is { } next:
                        pending.Enqueue(next);
                        break;
                }
            }
        }
        return names.ToFrozenSet(StringComparer.Ordinal);
    }

    private static Stage? FindStage(Entity entity, string name) =>
        entity.Stages.FirstOrDefault(stage => string.Equals(stage.Name, name, StringComparison.Ordinal));
}