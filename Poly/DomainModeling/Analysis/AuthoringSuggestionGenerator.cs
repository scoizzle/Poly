using System;

using Poly.DomainModeling;
using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Generates simple authoring suggestions. This is a minimal implementation
/// that uses only known-good APIs to ensure it compiles.
/// </summary>
public sealed class AuthoringSuggestionGenerator : INodeAnalyzer {
    public static string PassId => "DomainAuthoringSuggestionGenerator";
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Entity entity && entity.Actions.Count > 5) {
            context.ReportHint(
                entity,
                $"Entity '{entity.Name}' has many actions ({entity.Actions.Count}). Consider whether stages would help organize behavior.",
                "MANY_ACTIONS_CONSIDER_STAGES");
        }

        this.AnalyzeChildren(context, node);
    }
}