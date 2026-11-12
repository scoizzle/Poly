using System.Collections.Frozen;

using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The analysis-side answer to "which properties does a new instance's constructor assign beyond
/// its parameters" (the create-time set, Q12 option (b)).
/// <para>
/// The walk mirrors the constructor's lowering of the initial stage's entry: an
/// <see cref="AssignEffect"/> whose target is a <see cref="PropertyAccess"/> contributes its
/// authored name; a <see cref="StageTransitionEffect"/> to a resolvable stage T exits its source
/// stage, moves the source cursor to T, walks T's entry unless T is already being entered, and
/// adds the lifecycle property (G7); a self <see cref="InvokeActionEffect"/> contributes the
/// callee's assigns, transitively. The cursor is the lowering's: null (every stage's exit) after a
/// hop-holding branch or <c>for</c>, after every self invoke, and at the start of a <c>for</c>
/// body; an else branch restarts from the conditional's entry stage. The tests compare the fact
/// against the lowered constructor for every one of those shapes.
/// </para>
/// <para>
/// May-assign; invoked-action transitions are not followed; not for definite-initialization
/// decisions. Conditional and loop bodies count even when they cannot execute, so a create-supplied
/// value for such a property is an error the author must escape with <c>default(x)</c>. Exits
/// that never fire (dead per-stage switch arms) count the same way. Names are
/// returned verbatim, never filtered against <see cref="Entity.Properties"/>, so an assignment to a
/// mistyped name is not silently swallowed; a non-<see cref="PropertyAccess"/> target
/// (a <see cref="RelationshipNavigation"/>) is skipped because it names another entity's property.
/// </para>
/// <para>
/// This is the analysis-side provider. S-14 consumes it ONLY for may-assign, fail-closed decisions
/// (rejecting a create-supplied value); the flat
/// <see cref="EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames"/> stays the definite helper
/// for every relief use. S-14's lock must acknowledge the invoked-action gap. No lowering code calls
/// <see cref="StageEntryFacts"/> (<c>docs/CORE.md</c>; <c>RequireStructure</c>).
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

        var walk = new CreateWalk(entity, LifecyclePropertyName(entity, domain));
        walk.Effects(initial.OnEntryEffects);
        return walk.Names.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The lifecycle property (G7): the one property typed as a domain enum whose member names equal the
    /// stage names. None for zero or more than one candidate; analysis rejects the latter
    /// (<see cref="EntityStructureAnalyzer"/> throws), so the fact never guesses.
    /// </summary>
    internal static string? LifecyclePropertyName(Entity entity, Domain domain) {
        if (entity.Stages.Count == 0 || EntityStructureAnalyzer.IsImplicitStage(entity, domain))
            return null;
        var stageNames = entity.Stages.Select(stage => stage.Name).ToHashSet(StringComparer.Ordinal);
        string? found = null;
        foreach (var property in entity.Properties) {
            if (domain.Types.OfType<EnumType>().FirstOrDefault(e => string.Equals(e.Name, property.Type.TypeName, StringComparison.Ordinal)) is not { } enumType
                || enumType.MemberNames.Count != stageNames.Count
                || !enumType.MemberNames.All(stageNames.Contains))
                continue;
            if (found is not null)
                return null;
            found = property.Name;
        }
        return found;
    }

    /// <summary>
    /// Mirrors the constructor's lowering of the initial stage's entry (<c>EffectLoweringPass</c>
    /// <c>StageTransition</c>). <see cref="_cursor"/> is the lowering's source-stage cursor: null
    /// is the dynamic per-stage exit switch, which takes every stage's exit. Each hop to T runs
    /// its source's exit, moves the cursor to T, then walks T's entry unless T is already being
    /// entered. The initial entry is walked unpushed, as the constructor does.
    /// </summary>
    private sealed class CreateWalk(Entity entity, string? lifecycleProperty) {
        private readonly HashSet<string> _entering = new(StringComparer.Ordinal);
        private readonly HashSet<string> _exiting = new(StringComparer.Ordinal);
        private readonly HashSet<string> _invoked = new(StringComparer.Ordinal);
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
                case InvokeActionEffect { TargetRelationship: null } invoke:
                    SelfInvoke(invoke);
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
        // ones), which at most drops names where the lowering itself would recurse forever.
        private void ForEach(ForEachEffect loop) {
            var outer = _cursor;
            _cursor = null;
            Effects(loop.Body);
            _cursor = CanChangeStage(loop.Body) ? null : outer;
        }

        // A callee may transition, so the cursor goes dynamic. The callee's own assigns (and its
        // self-invokes, transitively) count; its transitions are not followed (recorded gap F-1).
        private void SelfInvoke(InvokeActionEffect invoke) {
            _cursor = null;
            CollectActionAssigns(invoke.ActionName);
        }

        private void CollectActionAssigns(string actionName) {
            if (!_invoked.Add(actionName))
                return;
            var actions = entity.Actions.Concat(entity.Stages.SelectMany(stage => stage.Actions))
                .Where(action => string.Equals(action.Name, actionName, StringComparison.Ordinal));
            foreach (var action in actions) {
                foreach (var effect in EffectHelpers.FlattenEffects(action.Effects)) {
                    switch (effect) {
                        case AssignEffect { Target: PropertyAccess property }:
                            Names.Add(property.Name);
                            break;
                        case InvokeActionEffect { TargetRelationship: null } callee:
                            CollectActionAssigns(callee.ActionName);
                            break;
                    }
                }
            }
        }

        private void Hop(StageTransitionEffect transition) {
            // Lower's fail-closed boundary: a hop to a stage the entity lacks throws.
            if (FindStage(transition.TargetStage.StageName) is not { } target)
                throw new InvalidOperationException(
                    $"Stage '{transition.TargetStage.StageName}' does not exist on entity '{entity.Name}'.");

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

            // G7: every resolved hop keeps the lifecycle property in lockstep with CurrentStage.
            if (lifecycleProperty is not null)
                Names.Add(lifecycleProperty);
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