using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

public sealed record ActionCapabilityView(
    string ActionName,
    IReadOnlyList<Property> Parameters,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<Type> EffectTypes,
    IReadOnlyList<Stage> TransitionTargets,
    IReadOnlyList<Policy> EffectivePolicies);

/// <summary>
/// Canonical stage-effective surface. Composition rules live in
/// <see cref="DomainEffectiveSurface"/> — entity+stage policies; stage-local actions.
/// </summary>
public sealed record StageCapabilityView(
    string StageName,
    IReadOnlyList<ActionCapabilityView> LocalActions,
    IReadOnlyList<ActionCapabilityView> EffectiveActions,
    IReadOnlyList<Policy> LocalPolicies,
    IReadOnlyList<Policy> EffectivePolicies);

internal sealed record ActionCapabilityMetadata(ActionCapabilityView View) : IAnalysisMetadata;
internal sealed record StageCapabilityMetadata(StageCapabilityView View) : IAnalysisMetadata;

/// <summary>
/// Publishes the canonical capability surface: per-action transition targets
/// (real <see cref="Stage"/> refs from the catalog) and per-stage effective
/// actions/policies via <see cref="DomainEffectiveSurface"/>.
/// </summary>
internal sealed class CapabilityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainCapabilityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                return;
            case Action action:
                AnalyzeAction(context, action);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                AnalyzeAction(context, action, domain, entity, entity.Policies);
            }
            foreach (var stage in entity.Stages) {
                var stagePolicies = DomainEffectiveSurface.ComposeStagePolicies(entity.Policies, stage);
                foreach (var action in stage.Actions) {
                    AnalyzeAction(context, action, domain, entity, stagePolicies);
                }
                AnalyzeStage(context, stage, entity);
            }
        });
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        var ownerEntity = context.GetMetadata<OwnerEntityMetadata>(action)?.Owner;
        Domain? domain = context.GetTypeLookup()?.Domain;
        AnalyzeAction(context, action, domain, ownerEntity, ownerEntity?.Policies ?? Array.Empty<Policy>());
    }

    private static void AnalyzeAction(
        AnalysisContext context,
        Action action,
        Domain? domain,
        Entity? ownerEntity,
        IReadOnlyList<Policy> inheritedPolicies) {
        var stagesByName = ResolveOwnerStages(context, domain, ownerEntity);

        var transitionTargetStages = new List<Stage>();
        foreach (var effect in FlattenEffects(action.Effects)) {
            if (effect is not StageTransitionEffect ste)
                continue;
            // Real Stage refs from catalog only — no empty stub stages.
            if (stagesByName is not null
                && stagesByName.TryGetValue(ste.TargetStage.StageName, out var resolved))
                transitionTargetStages.Add(resolved);
        }

        var effectivePolicies = inheritedPolicies.Count == 0
            ? action.Policies
            : [.. inheritedPolicies, .. action.Policies];

        var view = new ActionCapabilityView(
            ActionName: action.Name,
            Parameters: action.Parameters,
            Effects: action.Effects,
            EffectTypes: action.Effects.Select(static e => e.GetType()).Distinct().ToArray(),
            TransitionTargets: transitionTargetStages,
            EffectivePolicies: effectivePolicies);

        context.SetMetadata(action, new ActionCapabilityMetadata(view));
    }

    private static IReadOnlyDictionary<string, Stage>? ResolveOwnerStages(
        AnalysisContext context,
        Domain? domain,
        Entity? ownerEntity) {
        if (ownerEntity is null)
            return null;

        if (domain is not null) {
            var catalog = context.GetMetadata<DomainCatalogMetadata>(domain);
            if (catalog is not null
                && catalog.Index.StagesByEntity.TryGetValue(ownerEntity.Name, out var fromCatalog))
                return fromCatalog;
        }

        // Catalog absent mid-pipeline / tests: use the entity's own stage map (same nodes).
        return ownerEntity.Stages
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        var owner = context.GetMetadata<OwnerEntityMetadata>(stage)?.Owner;
        AnalyzeStage(context, stage, owner);
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage, Entity? ownerEntity) {
        var localActionViews = stage.Actions
            .Select(a => context.GetMetadata<ActionCapabilityMetadata>(a)?.View)
            .OfType<ActionCapabilityView>()
            .ToArray();

        var entityPolicies = ownerEntity?.Policies ?? Array.Empty<Policy>();
        var effectivePolicies = DomainEffectiveSurface.ComposeStagePolicies(entityPolicies, stage);

        // Stage hierarchy not supported — effective actions are stage-local only.
        var view = new StageCapabilityView(
            StageName: stage.Name,
            LocalActions: localActionViews,
            EffectiveActions: localActionViews,
            LocalPolicies: stage.Policies,
            EffectivePolicies: effectivePolicies);

        context.SetMetadata(stage, new StageCapabilityMetadata(view));
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects);
}