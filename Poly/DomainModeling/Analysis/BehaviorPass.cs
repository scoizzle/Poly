using Poly.Analysis;
using Poly.DomainModeling.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Thin pack DTO adapter: projects already-analyzed capability facts into
/// <see cref="BehaviorMetadata"/> for codegen. Does not re-compose stage-effective
/// policies/actions or re-walk effects for transitions — those live on the
/// Capability surface only (action effective policies + transition targets).
/// Depends on <see cref="SemanticDomainAnalyzer"/> (type resolution) and
/// <see cref="CapabilityAnalyzer"/> (the capability surface).
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

        // Action-level effective policy names (entity/stage + action) from the
        // canonical capability surface — one producer, no dual composition path.
        // The capability is always present in the pipeline (CapabilityAnalyzer
        // dependency); the previous action.Policies fallback was unreachable.
        var capability = context.GetMetadata<ActionCapabilityMetadata>(action);
        var policies = capability?.View.EffectivePolicies.Select(p => p.Name).ToList() ?? [];

        var parameters = action.Parameters.Select(p => {
            var isEntityRef = IsEntityRefParam(context, p, entityLookup);
            var isRequired = p.Constraints.Any(c => c is RequiredConstraint);
            return new BehaviorParameter(p.Name, p.Type.TypeName, isRequired, isEntityRef);
        }).ToList();

        // Transitions from ActionCapability only — no effect-walk dual path.
        var transitions = new List<StageTransitionTarget>();
        if (capability is not null) {
            foreach (var stage in capability.View.TransitionTargets)
                transitions.Add(new StageTransitionTarget(stage.Name));
        }

        return new BehaviorAction(entity.Name, stageName, action.Name, parameters, isVoid, resultTypeName, policies, transitions);
    }

    private static bool IsEntityRefParam(AnalysisContext context, Property param, Dictionary<string, Entity> entityLookup) {
        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(param.Type);
        if (resolved is not null)
            return resolved.Type is Entity;
        return entityLookup.ContainsKey(param.Type.TypeName);
    }

    /// <summary>
    /// Builds a <see cref="BehaviorModel"/> via the domain analysis pipeline
    /// (Capability + EPM → pack DTO). No offline dual composition path.
    /// </summary>
    internal static BehaviorModel BuildBehavior(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        return analysis.GetMetadata<BehaviorMetadata>(domain)?.Behavior
            ?? new BehaviorModel(domain.Name, []);
    }
}