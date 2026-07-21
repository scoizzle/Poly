using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Builds <see cref="BehaviorModel"/> — per-entity action metadata.
///
/// Consumes <see cref="EffectivePoliciesMetadata"/> for correct policy inheritance,
/// <see cref="ActionCapabilityMetadata"/> for pre-computed stage transitions,
/// and <see cref="DomainTypeLookupMetadata"/> for entity-ref detection.
///
/// This is a derived domain fact, not a transport convention.
/// Protocol-specific codegens consume the resulting BehaviorAction records
/// and map them to endpoints, mutations, or RPCs.
/// </summary>
public sealed class BehaviorAnalyzer {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly Dictionary<string, Entity> _entityLookup;
    private readonly AnalysisResult? _analysis;

    public BehaviorAnalyzer(Domain domain, AnalysisResult? analysis = null) {
        _domain = domain;
        _analysis = analysis;

        var lookup = analysis?.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is not null) {
            _entities = lookup.Entities.ToList();
            _entityLookup = lookup.Types
                .Where(kvp => kvp.Value is Entity)
                .ToDictionary(kvp => kvp.Key, kvp => (Entity)kvp.Value, StringComparer.Ordinal);
        }
        else {
            _entities = domain.Types.OfType<Entity>().ToList();
            _entityLookup = _entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        }
    }

    public BehaviorModel Analyze() {
        var behaviorEntities = new List<BehaviorEntity>();

        foreach (var entity in _entities) {
            var actions = new List<BehaviorAction>();

            foreach (var action in entity.Actions)
                actions.Add(BuildBehaviorAction(entity, action, stageName: null));

            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions)
                    actions.Add(BuildBehaviorAction(entity, action, stage.Name));
            }

            behaviorEntities.Add(new BehaviorEntity(entity.Name, actions));
        }

        return new BehaviorModel(_domain.Name, behaviorEntities);
    }

    private BehaviorAction BuildBehaviorAction(Entity entity, DomainModeling.Action action, string? stageName) {
        var isVoid = action.Result.Members.Count == 0;
        var resultTypeName = isVoid ? null : action.Result.Members[0].Type.TypeName;

        var policies = new List<string>();
        var effectivePolicies = _analysis?.GetMetadata<EffectivePoliciesMetadata>(action);
        if (effectivePolicies is not null) {
            foreach (var p in effectivePolicies.Policies)
                policies.Add(p.Name);
        }
        else {
            foreach (var p in action.Policies)
                policies.Add(p.Name);
        }

        var parameters = action.Parameters.Select(p => {
            var isEntityRef = IsEntityRefParam(p);
            var isRequired = p.Constraints.Any(c => c is RequiredConstraint);
            return new BehaviorParameter(p.Name, p.Type.TypeName, isRequired, isEntityRef);
        }).ToList();

        var transitions = new List<StageTransitionTarget>();
        var capability = _analysis?.GetMetadata<ActionCapabilityMetadata>(action);
        if (capability is not null) {
            foreach (var stage in capability.View.TransitionTargets)
                transitions.Add(new StageTransitionTarget(stage.Name));
        }
        else {
            foreach (var effect in action.Effects)
                WalkEffectsForTransitions(effect, transitions);
        }

        return new BehaviorAction(
            entity.Name,
            stageName,
            action.Name,
            parameters,
            isVoid,
            resultTypeName,
            policies,
            transitions
        );
    }

    private bool IsEntityRefParam(Property param) {
        var resolved = _analysis?.GetMetadata<ResolvedTypeReferenceMetadata>(param.Type);
        if (resolved is not null)
            return resolved.Type is Entity;
        return _entityLookup.ContainsKey(param.Type.TypeName);
    }

    private static void WalkEffectsForTransitions(Effect effect, List<StageTransitionTarget> targets) {
        if (effect is StageTransitionEffect ste)
            targets.Add(new StageTransitionTarget(ste.TargetStage.StageName));
        if (effect is CompositeEffect ce)
            foreach (var child in ce.Effects)
                WalkEffectsForTransitions(child, targets);
        if (effect is ConditionalEffect cond) {
            foreach (var e in cond.ThenEffects)
                WalkEffectsForTransitions(e, targets);
            if (cond.ElseEffects is not null)
                foreach (var e in cond.ElseEffects)
                    WalkEffectsForTransitions(e, targets);
        }
    }
}