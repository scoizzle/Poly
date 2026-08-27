using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Vocabulary bag: this unit loaded <c>uses temporal</c>. Checks, elaboration, and
/// lowering consume analysis — not a session Meaning table.
/// </summary>
public sealed record TemporalVocabularyMetadata : IAnalysisMetadata;

/// <summary>Registers temporal vocabulary on the domain when the library is loaded.</summary>
public sealed class TemporalPass : INodeAnalyzer {
    public const string Id = "Temporal";
    public string PassName => Id;
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain)
            return;

        context.SetMetadata(domain, new TemporalVocabularyMetadata());
        StampAssignConversions(context, domain);
    }

    private static void StampAssignConversions(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity => {
            foreach (var action in entity.Actions)
                StampEffects(context, action.Effects, entity);
            foreach (var sub in entity.Subscriptions)
                StampEffects(context, sub.Effects, entity);
            foreach (var stage in entity.Stages) {
                StampEffects(context, stage.OnEntryEffects, entity);
                StampEffects(context, stage.OnExitEffects, entity);
                foreach (var action in stage.Actions)
                    StampEffects(context, action.Effects, entity);
                foreach (var sub in stage.Subscriptions)
                    StampEffects(context, sub.Effects, entity);
            }
        });
    }

    private static void StampEffects(AnalysisContext context, IEnumerable<Effect> effects, Entity entity) {
        foreach (var effect in EffectHelpers.FlattenEffects(effects)) {
            if (effect is not AssignEffect assign)
                continue;
            if (assign.Target is not PropertyAccess dest
                || assign.Value is not PropertyAccess src)
                continue;

            var destType = TypeOf(entity, dest.Name);
            var srcType = TypeOf(entity, src.Name);
            if (destType is null || srcType is null)
                continue;

            if (IsDateTimeName(destType) && IsDateName(srcType)) {
                context.SetMetadata(assign, new AssignedMemberConversionMetadata(
                    "ToDateTime",
                    [new AssignedMemberConversionArgument("TimeOnly", "MinValue")]));
            }
        }
    }

    private static string? TypeOf(Entity entity, string propertyName) =>
        entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal))?.Type.TypeName;

    private static bool IsDateName(string typeName) => typeName is "Date" or "DateOnly";

    private static bool IsDateTimeName(string typeName) => typeName is "DateTime" or "Timestamp";
}