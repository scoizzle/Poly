using Poly.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

public sealed record ActionCapabilityView(
    string ActionName,
    IReadOnlyList<Property> Parameters,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<Type> EffectTypes,
    IReadOnlyList<Stage> TransitionTargets);

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

public sealed record RelationshipCapabilityView(
    string RelationshipName,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties,
    IReadOnlyList<Stage> Stages,
    IReadOnlyList<Policy> Policies);

internal sealed record ActionCapabilityMetadata(ActionCapabilityView View) : IAnalysisMetadata;
internal sealed record StageCapabilityMetadata(StageCapabilityView View) : IAnalysisMetadata;
internal sealed record RelationshipCapabilityMetadata(RelationshipCapabilityView View) : IAnalysisMetadata;

/// <summary>
/// Publishes the canonical capability surface: per-action transition targets
/// (real <see cref="Stage"/> refs from the catalog) and per-stage effective
/// actions/policies via <see cref="DomainEffectiveSurface"/>.
/// </summary>
internal sealed class CapabilityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainCapabilityAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id, DomainCatalogPass.Id];

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
            case Relationship relationship:
                AnalyzeRelationship(context, relationship);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                AnalyzeAction(context, action, domain, entity);
            }
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions) {
                    AnalyzeAction(context, action, domain, entity);
                }
                AnalyzeStage(context, stage, entity);
            }
        });

        foreach (var relationship in domain.Relationships) {
            AnalyzeRelationship(context, relationship);
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var ownerEntity = FindOwnerEntity(lookup, action);
        Domain? domain = lookup?.Domain;
        AnalyzeAction(context, action, domain, ownerEntity);
    }

    private static void AnalyzeAction(
        AnalysisContext context,
        Action action,
        Domain? domain,
        Entity? ownerEntity) {
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

        var view = new ActionCapabilityView(
            ActionName: action.Name,
            Parameters: action.Parameters,
            Effects: action.Effects,
            EffectTypes: action.Effects.Select(static e => e.GetType()).Distinct().ToArray(),
            TransitionTargets: transitionTargetStages);

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

    private static Entity? FindOwnerEntity(DomainTypeLookupMetadata? lookup, Action action) {
        if (lookup is null) return null;
        foreach (var entity in lookup.Entities) {
            if (entity.Actions.Contains(action)) return entity;
            foreach (var stage in entity.Stages) {
                if (stage.Actions.Contains(action)) return entity;
            }
        }
        return null;
    }

    private static Entity? FindOwnerEntityForStage(DomainTypeLookupMetadata? lookup, Stage stage) {
        if (lookup is null) return null;
        foreach (var entity in lookup.Entities) {
            if (entity.Stages.Contains(stage)) return entity;
        }
        return null;
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var owner = FindOwnerEntityForStage(lookup, stage);
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

    private static void AnalyzeRelationship(AnalysisContext context, Relationship relationship) {
        var view = new RelationshipCapabilityView(
            RelationshipName: relationship.Name,
            Source: relationship.Source,
            Target: relationship.Target,
            Cardinality: relationship.Cardinality,
            Properties: relationship.Properties,
            Stages: relationship.Stages,
            Policies: relationship.Policies);

        context.SetMetadata(relationship, new RelationshipCapabilityMetadata(view));
    }

    private static IEnumerable<Effect> FlattenEffects(IEnumerable<Effect> effects) =>
        EffectHelpers.FlattenEffects(effects);
}