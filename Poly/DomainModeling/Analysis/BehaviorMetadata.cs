using Poly.Analysis;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Per-entity action metadata (parameters, return types, effective policies, stage transitions).
/// Projected from capability facts at emit/read time — not a pipeline pass.
/// </summary>
public sealed record BehaviorMetadata(BehaviorModel Behavior) {
    /// <summary>
    /// Builds the codegen DTO from capability + type-lookup already on
    /// <paramref name="analysis"/>. Empty when the analysis has no entities.
    /// </summary>
    public static BehaviorModel From(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);

        var lookup = analysis.GetTypeLookup(domain)
            ?? analysis.GetTypeLookup();
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
                actions.Add(BuildAction(analysis, entity, action, entityLookup, stageName: null));
            foreach (var stage in entity.Stages)
                foreach (var action in stage.Actions)
                    actions.Add(BuildAction(analysis, entity, action, entityLookup, stage.Name));

            behaviorEntities.Add(new BehaviorEntity(entity.Name, actions));
        }

        return new BehaviorModel(domain.Name, behaviorEntities);
    }

    internal static BehaviorModel BuildBehavior(Domain domain) {
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        return From(domain, analysis);
    }

    private static BehaviorAction BuildAction(
        AnalysisResult analysis, Entity entity, Action action,
        Dictionary<string, Entity> entityLookup, string? stageName) {
        var isVoid = action.Result.Members.Count == 0;
        var resultTypeName = isVoid ? null : action.Result.Members[0].Type.TypeName;

        var capability = analysis.GetMetadata<ActionCapabilityMetadata>(action);
        var policies = capability?.View.EffectivePolicies.Select(p => p.Name).ToList() ?? [];

        var parameters = action.Parameters.Select(p => {
            var isEntityRef = IsEntityRefParam(analysis, p, entityLookup);
            var isRequired = p.Constraints.Any(c => c is RequiredConstraint);
            return new BehaviorParameter(p.Name, p.Type.TypeName, isRequired, isEntityRef);
        }).ToList();

        var transitions = new List<StageTransitionTarget>();
        if (capability is not null) {
            foreach (var stage in capability.View.TransitionTargets)
                transitions.Add(new StageTransitionTarget(stage.Name));
        }

        return new BehaviorAction(entity.Name, stageName, action.Name, parameters, isVoid, resultTypeName, policies, transitions);
    }

    private static bool IsEntityRefParam(AnalysisResult analysis, Property param, Dictionary<string, Entity> entityLookup) {
        var resolved = analysis.GetMetadata<ResolvedTypeReferenceMetadata>(param.Type);
        if (resolved is not null)
            return resolved.Type is Entity;
        return entityLookup.ContainsKey(param.Type.TypeName);
    }
}