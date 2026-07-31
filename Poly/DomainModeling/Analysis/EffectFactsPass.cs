using Poly.Analysis;
using Poly.DomainModeling.Effects;

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
    public string[] Dependencies => [SemanticDomainAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        if (node is Domain domain) {
            PublishDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
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
                && TryResolveCreateIn(createIn, entity, domain, lookup, out var relationship, out var targetEntity)) {
                context.SetMetadata(createIn, new ResolvedRelationshipTargetMetadata(relationship, targetEntity));
            }
        }
    }

    /// <summary>
    /// Shared resolve for create-in facts (and validate pack when it wants the same answer).
    /// Succeeds only when relationship exists, source matches the effect owner, and target is an entity.
    /// </summary>
    internal static bool TryResolveCreateIn(
        CreateEntityInRelationshipEffect createIn,
        Entity sourceEntity,
        Domain domain,
        DomainTypeLookupMetadata lookup,
        out Relationship relationship,
        out Entity targetEntity) {

        relationship = null!;
        targetEntity = null!;

        var rel = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, createIn.RelationshipName, StringComparison.Ordinal));
        if (rel is null)
            return false;

        if (!string.Equals(rel.Source.TypeName, sourceEntity.Name, StringComparison.Ordinal))
            return false;

        if (!lookup.Types.TryGetValue(rel.Target.TypeName, out var targetType)
            || targetType is not Entity target)
            return false;

        relationship = rel;
        targetEntity = target;
        return true;
    }
}