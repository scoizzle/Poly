using System;

using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Detects basic idempotency concerns in actions.
/// This is a minimal implementation that uses only known-good APIs.
/// </summary>
public sealed class IdempotencySafetyAnalyzer : INodeAnalyzer {
    public const string Id = "DomainIdempotencySafety";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Action action && action.Name.Contains("Create", StringComparison.OrdinalIgnoreCase)) {
            context.ReportHint(
                action,
                $"Action '{action.Name}' creates data. Consider whether it should have explicit validation or idempotency guarantees.",
                "CREATE_ACTION_CONSIDER_VALIDATION");
        }

        this.AnalyzeChildren(context, node);
    }
}