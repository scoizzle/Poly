using System;

using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analyzes the domain for basic semantic coherence.
/// This is a minimal implementation that uses only known-good APIs.
/// </summary>
public sealed class SemanticCoherenceAnalyzer : INodeAnalyzer {
    public static string PassId => "DomainSemanticCoherence";
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain && domain.Types.Count > 1) {
            context.ReportHint(
                domain,
                "Domain contains multiple types. Consider whether the model would benefit from additional relationships or constraints.",
                "MULTIPLE_TYPES_CONSIDER_RELATIONSHIPS");
        }

        this.AnalyzeChildren(context, node);
    }
}