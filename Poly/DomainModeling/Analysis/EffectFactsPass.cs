using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Fact emitter: publishes <see cref="ResolvedRelationshipTargetMetadata"/> on
/// <see cref="CreateEntityInRelationshipEffect"/> nodes when the relationship and
/// target entity resolve. Effect binding/validation diagnostics remain in
/// <see cref="EffectAnalyzer"/> (validate pack).
/// </summary>
internal sealed class EffectFactsPass : INodeAnalyzer {
    public const string Id = "DomainEffectFacts";
    public string PassName => Id;
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            PublishDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup();
        if (lookup is null)
            return;

        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions) {
                PublishEffects(context, action.Effects, entity, domain, lookup);
            }
            foreach (var stage in entity.Stages) {
                PublishEffects(context, stage.OnEntryEffects, entity, domain, lookup);
                PublishEffects(context, stage.OnExitEffects, entity, domain, lookup);
            }
        });
    }

    private static void PublishEffects(
        AnalysisContext context,
        IReadOnlyList<Effect> effects,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {

        foreach (var effect in EffectHelpers.FlattenEffects(effects)) {
            if (effect is CreateEntityInRelationshipEffect createIn
                && TryResolveCreateIn(context, createIn, entity, domain, lookup, out var relationship, out var targetEntity)) {
                context.SetMetadata(createIn, new ResolvedRelationshipTargetMetadata(relationship, targetEntity));
            }
        }
    }

    /// <summary>
    /// Shared resolve for create-in facts (and validate pack when it wants the same answer).
    /// Succeeds only when relationship exists, source matches the effect owner, and target is an entity.
    /// Resolves the relationship through the catalog/RLM bags (amu-w1-1 sibling; review F3) —
    /// no <c>domain.Relationships</c> tree scan — so facts and the validate pack agree
    /// under the same semantic source (ResolvedRelationshipTargetMetadata).
    /// </summary>
    internal static bool TryResolveCreateIn(
        AnalysisContext context,
        CreateEntityInRelationshipEffect createIn,
        Entity sourceEntity,
        Domain domain,
        DomainTypeLookupMetadata lookup,
        out Relationship relationship,
        out Entity targetEntity) {

        relationship = null!;
        targetEntity = null!;

        var relLookup = context.GetRelationshipLookup(domain)
            ?? context.GetRelationshipLookup();
        if (relLookup is null)
            return false;

        if (!relLookup.TryGetRelationship(sourceEntity.Name, createIn.RelationshipName, out var rel))
            return false;

        if (!lookup.Types.TryGetValue(rel.Target.TypeName, out var targetType)
            || targetType is not Entity target)
            return false;

        relationship = rel;
        targetEntity = target;
        return true;
    }
}