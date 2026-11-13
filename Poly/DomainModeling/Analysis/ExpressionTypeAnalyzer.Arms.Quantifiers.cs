using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Quantifier arm: RejectEndpointCallsInBody, and the body of any/all/none/count walked in the target entity's scope (A10).

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
    /// <summary>
    /// The body of <c>any</c>, <c>all</c>, <c>none</c> or a filtered <c>count</c> is a condition on the related entity:
    /// it is walked and typed in that entity's scope (its own properties, the action's parameters) and must be Boolean.
    /// Without an entity to range over there is nothing to walk; the consumer records the quantifier for DMSEM006.
    /// </summary>
    private void CheckQuantifierBody(
        AnalysisContext context, DomainExpression quantifier,
        Dictionary<string, string> props, Dictionary<string, string>? parameters, Dictionary<string, EnumType> enumTypes) {
        var (keyword, body) = quantifier switch {
            AnyExpr a => ("any", a.Body),
            AllExpr a => ("all", a.Body),
            NoneExpr n => ("none", n.Body),
            CountExpr c => ("count", c.Body),
            _ => ("", null),
        };
        if (body is null || QuantifiedEntity(context, quantifier, props) is not { } target) return;
        var scope = new EntityScope(target);
        WalkExpression(context, body, scope, parameters, enumTypes);
        var bodyType = InferType(context, body, scope, parameters, enumTypes);
        if (!IsTyped(bodyType))
            RequireTyped(context, body, bodyType, quantifier, scope, parameters, enumTypes);
        else if (Family(bodyType) is not TypeFamily.Boolean)
            Report(context, body, $"'{keyword}' condition must be Boolean (got '{Describe(bodyType)}')");
    }

    private static void RejectEndpointCallsInBody(AnalysisContext context, Node node) {
        foreach (var child in node.Children) {
            if (child is FunctionCall call
                && FunctionAnalysis.Find(context, call.Name) is null
                && context.GetTypeLookup()?.Domain is { } domain
                && ContractCallResolver.IsExported(domain, call.Name))
                ContractCallResolver.RejectUnlowerable(context, call);
            if (child is not null)
                RejectEndpointCallsInBody(context, child);
        }
    }
}