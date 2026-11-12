using Poly.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

namespace Poly.DomainModeling.Analysis;

// EffectAnalyzer (Link) — legality of `link Rel to binder` / `unlink Rel from binder` (#407, F-16).
internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// Checks one <c>link</c> or <c>unlink</c> effect. A malformed effect reports its own DMEFF001
    /// and stops; a well-formed one is refused with DMEFF020 because nothing lowers it yet (L-13).
    /// The target must be a bare <c>for</c> binder in scope; an owned navigation is refused because
    /// its owner is fixed.
    /// </summary>
    private static void ValidateLinkEffect(
        AnalysisContext context, Effect effect, string relationshipName, DomainExpression target,
        string form, Entity entity, Domain domain, IReadOnlyDictionary<string, string?>? forBinders) {
        if (!TryResolveRelationship(context, domain, entity.Name, relationshipName, effect, out var relationship))
            return;
        if (relationship is null) {
            var elsewhere = ResolveRelationshipLookup(context, domain)?.FindByNameAcrossSources(relationshipName).FirstOrDefault();
            context.ReportError(
                effect,
                elsewhere is null
                    ? $"'{form}' references relationship '{relationshipName}' which does not exist on entity '{entity.Name}'."
                    : $"'{form}' relationship '{relationshipName}' is declared on source entity " +
                      $"'{elsewhere.Source.TypeName}', not '{entity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }
        if (relationship.SourceOwnsTarget) {
            context.ReportError(
                effect,
                $"'{form}' cannot use owned navigation '{relationshipName}': an owned navigation is written only by " +
                $"'create in {relationshipName}'; its owner is fixed. Delete and create to move.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }
        if (target is not PropertyAccess { Name: var binder } || forBinders?.ContainsKey(binder) != true) {
            context.ReportError(
                effect,
                $"'{form}' on '{relationshipName}' needs a 'for' binder in scope as its target " +
                "(for x in Rel { … x … }); any other expression is not supported.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }
        if (forBinders[binder] is { } binderType
            && !string.Equals(binderType, relationship.Target.TypeName, StringComparison.Ordinal)) {
            context.ReportError(
                effect,
                $"'{form}' binder '{binder}' is a '{binderType}', but relationship '{relationshipName}' " +
                $"holds '{relationship.Target.TypeName}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }
        context.ReportError(
            effect,
            $"'{form}' parses and prints but is not lowerable yet (L-13); Analyze rejects it so nothing unlowerable ships.",
            DomainModelDiagnosticCodes.EffectLinkNotLowerable);
    }
}
