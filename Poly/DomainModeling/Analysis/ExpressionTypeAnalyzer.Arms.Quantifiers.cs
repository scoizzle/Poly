using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

// Holds RejectEndpointCallsInBody only; the quantifier/navigation arm cases stay in core's WalkExpression.

internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
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