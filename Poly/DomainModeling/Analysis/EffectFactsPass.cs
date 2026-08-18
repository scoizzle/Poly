using Poly.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

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
        if (!context.ShouldAnalyze(node))
            return;

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