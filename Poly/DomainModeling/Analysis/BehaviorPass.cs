using Poly.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analysis pass that produces <see cref="BehaviorMetadata"/> —
/// per-entity action metadata (parameters, return types, effective policies, stage transitions).
///
/// Consumes <see cref="EffectivePoliciesMetadata"/> for correct policy inheritance,
/// <see cref="ActionCapabilityMetadata"/> for pre-computed stage transitions,
/// and <see cref="DomainTypeLookupMetadata"/> for entity-ref detection.
///
/// This is a derived domain fact, not a transport convention.
/// Protocol-specific codegens consume the resulting BehaviorAction records
/// and map them to endpoints, mutations, or RPCs.
/// Depends on <see cref="SemanticDomainAnalyzer"/> for type resolution and
/// <see cref="CapabilityAnalyzer"/> for action capability views.
/// </summary>
internal sealed class BehaviorPass : INodeAnalyzer {
    public const string Id = "BehaviorPass";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id, CapabilityAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.HasStructuralFailure) return;

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var entities = lookup is not null
            ? lookup.Entities.ToList()
            : domain.Types.OfType<Entity>().ToList();
        var entityLookup = lookup is not null
            ? lookup.Types.Where(kvp => kvp.Value is Entity)
                .ToDictionary(kvp => kvp.Key, kvp => (Entity)kvp.Value, StringComparer.Ordinal)
            : entities.ToDictionary(e => e.Name, StringComparer.Ordinal);

        var behaviorEntities = new List<BehaviorEntity>();
        foreach (var entity in entities) {
            var actions = new List<BehaviorAction>();

            foreach (var action in entity.Actions)
                actions.Add(BuildBehaviorAction(context, entity, action, entityLookup, stageName: null));
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    actions.Add(BuildBehaviorAction(context, entity, action, entityLookup, stage.Name));

            behaviorEntities.Add(new BehaviorEntity(entity.Name, actions));
        }

        var behavior = new BehaviorModel(domain.Name, behaviorEntities);
        context.SetMetadata(domain, new BehaviorMetadata(behavior));
    }

    private static BehaviorAction BuildBehaviorAction(
        AnalysisContext context, Entity entity, Action action,
        Dictionary<string, Entity> entityLookup, string? stageName) {
        var isVoid = action.Result.Members.Count == 0;
        var resultTypeName = isVoid ? null : action.Result.Members[0].Type.TypeName;

        var policies = new List<string>();
        var effectivePolicies = GetMetadata<EffectivePoliciesMetadata>(context, action);
        if (effectivePolicies is not null) {
            foreach (var p in effectivePolicies.Policies)
                policies.Add(p.Name);
        }
        else {
            foreach (var p in action.Policies)
                policies.Add(p.Name);
        }

        var parameters = action.Parameters.Select(p => {
            var isEntityRef = IsEntityRefParam(context, p, entityLookup);
            var isRequired = p.Constraints.Any(c => c is RequiredConstraint);
            return new BehaviorParameter(p.Name, p.Type.TypeName, isRequired, isEntityRef);
        }).ToList();

        var transitions = new List<StageTransitionTarget>();
        var capability = GetMetadata<ActionCapabilityMetadata>(context, action);
        if (capability is not null) {
            foreach (var stage in capability.View.TransitionTargets)
                transitions.Add(new StageTransitionTarget(stage.Name));
        }
        else {
            foreach (var effect in action.Effects)
                WalkEffectsForTransitions(effect, transitions);
        }

        return new BehaviorAction(entity.Name, stageName, action.Name, parameters, isVoid, resultTypeName, policies, transitions);
    }

    private static T? GetMetadata<T>(AnalysisContext context, Action action) where T : class, IAnalysisMetadata =>
        context.GetMetadata<T>(action);

    private static bool IsEntityRefParam(AnalysisContext context, Property param, Dictionary<string, Entity> entityLookup) {
        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(param.Type);
        if (resolved is not null)
            return resolved.Type is Entity;
        return entityLookup.ContainsKey(param.Type.TypeName);
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

    /// <summary>Builds a <see cref="BehaviorModel"/> outside the pipeline (for tests/legacy callers).</summary>
    internal static BehaviorModel BuildBehavior(Domain domain) {
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var behaviorEntities = new List<BehaviorEntity>();
        var dummyCtx = AnalysisContext.CreateDefault();
        foreach (var entity in entities) {
            var actions = new List<BehaviorAction>();
            foreach (var action in entity.Actions)
                actions.Add(BuildBehaviorAction(dummyCtx, entity, action, entityLookup, stageName: null));
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    actions.Add(BuildBehaviorAction(dummyCtx, entity, action, entityLookup, stage.Name));
            behaviorEntities.Add(new BehaviorEntity(entity.Name, actions));
        }
        return new BehaviorModel(domain.Name, behaviorEntities);
    }
}