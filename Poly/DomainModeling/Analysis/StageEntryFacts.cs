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
    /// Names of the entity's properties assigned at create time. Empty, never null, for an entity
    /// with no stages or only the implicit stage (<see cref="EntityStructureAnalyzer.IsImplicitStage"/>).
    /// </summary>
    public static IReadOnlySet<string> EntryAssignedProperties(Entity entity, Domain domain) {
        if (entity.InitialStage() is not { } initial || EntityStructureAnalyzer.IsImplicitStage(entity, domain))
            return FrozenSet<string>.Empty;

        var walk = new CreateWalk(entity);
        walk.Effects(initial.OnEntryEffects);
        return walk.Names.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Mirrors the constructor's lowering of the initial stage's entry (<c>EffectLoweringPass</c>
    /// <c>StageTransition</c>). <see cref="_cursor"/> is the lowering's source-stage cursor: null
    /// is the dynamic per-stage exit switch, which takes every stage's exit. Each hop to T runs
    /// its source's exit, moves the cursor to T, then walks T's entry unless T is already being
    /// entered. The initial entry is walked unpushed, as the constructor does.
    /// </summary>
    private sealed class CreateWalk(Entity entity) {
        private readonly HashSet<string> _entering = new(StringComparer.Ordinal);
        private readonly HashSet<string> _exiting = new(StringComparer.Ordinal);
        private string? _cursor = entity.InitialStage()!.Name;

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public void Effects(IEnumerable<Effect> effects) {
            foreach (var effect in effects)
                Walk(effect);
        }

        private void Walk(Effect effect) {
            switch (effect) {
                case AssignEffect { Target: PropertyAccess property }:
                    Names.Add(property.Name);
                    break;
                case CompositeEffect composite:
                    Effects(composite.Effects);
                    break;
                case ConditionalEffect conditional:
                    Conditional(conditional);
                    break;
                case ForEachEffect loop:
                    ForEach(loop);
                    break;
                case StageTransitionEffect transition:
                    Hop(transition);
                    break;
                case InvokeActionEffect { TargetRelationship: null }:
                    _cursor = null;
                    break;
            }
        }

        // The else branch restarts from the conditional's entry stage; a branch that can change
        // stage leaves the cursor dynamic afterwards.
        private void Conditional(ConditionalEffect conditional) {
            var canChange = CanChangeStage(conditional.ThenEffects)
                || (conditional.ElseEffects is { Count: > 0 } && CanChangeStage(conditional.ElseEffects));
            var entry = _cursor;
            Effects(conditional.ThenEffects);
            if (conditional.ElseEffects is { Count: > 0 }) {
                _cursor = entry;
                Effects(conditional.ElseEffects);
            }
            if (canChange)
                _cursor = null;
        }

        // The body is its own pass starting from the dynamic cursor; the outer cursor only goes
        // dynamic when the body can change stage. Guard stacks are shared (Lower builds fresh
        // ones), which can only drop names where the lowering itself would recurse forever.
        private void ForEach(ForEachEffect loop) {
            var outer = _cursor;
            _cursor = null;
            Effects(loop.Body);
            _cursor = CanChangeStage(loop.Body) ? null : outer;
        }

        private void Hop(StageTransitionEffect transition) {
            if (FindStage(transition.TargetStage.StageName) is not { } target)
                return;

            if (_cursor is not null) {
                if (FindStage(_cursor) is { } source)
                    Exit(source);
            }
            else {
                foreach (var stage in entity.Stages)
                    Exit(stage);
            }

            _cursor = target.Name;
            Enter(target);
        }

        private void Exit(Stage stage) {
            if (!_exiting.Add(stage.Name))
                return;
            try {
                Effects(stage.OnExitEffects);
            }
            finally {
                _exiting.Remove(stage.Name);
            }
        }

        private void Enter(Stage stage) {
            if (!_entering.Add(stage.Name))
                return;
            try {
                Effects(stage.OnEntryEffects);
            }
            finally {
                _entering.Remove(stage.Name);
            }
        }

        private Stage? FindStage(string name) =>
            entity.Stages.FirstOrDefault(stage => string.Equals(stage.Name, name, StringComparison.Ordinal));

        private static bool CanChangeStage(IEnumerable<Effect> effects) =>
            EffectHelpers.FlattenEffects(effects).Any(e =>
                e is StageTransitionEffect or InvokeActionEffect { TargetRelationship: null });
    }
}