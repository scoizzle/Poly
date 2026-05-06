namespace Poly.Syntax.Analysis;

public sealed record NodeInvalidityReason(
    string? Code,
    string Message,
    DiagnosticSeverity Severity,
    string Hint
);

public sealed record NodeInvalidityNodeReport(
    NodeId NodeId,
    string NodeType,
    string NodeName,
    IReadOnlyList<NodeInvalidityReason> Reasons
);

public sealed record NodeInvalidityReport(
    IReadOnlyList<NodeInvalidityNodeReport> Nodes,
    int ErrorCount,
    int WarningCount
);

public static class SyntaxInvalidityExplainer {
    public static NodeInvalidityReport Explain(
        AnalysisResult analysis,
        Func<string?, string, string>? buildHint = null,
        Func<Node, string>? getNodeName = null) {
        ArgumentNullException.ThrowIfNull(analysis);

        var resolveHint = buildHint ?? ((_, _) => "Review this diagnostic and adjust the referenced node configuration.");
        var resolveName = getNodeName ?? (node => node.Id.Value);

        var grouped = analysis.Diagnostics
            .GroupBy(static diagnostic => diagnostic.Node.Id)
            .OrderBy(static group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => {
                var node = group.First().Node;
                var reasons = group
                    .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
                    .Select(diagnostic => new NodeInvalidityReason(
                        diagnostic.Code,
                        diagnostic.Message,
                        diagnostic.Severity,
                        resolveHint(diagnostic.Code, diagnostic.Message)))
                    .ToArray();

                return new NodeInvalidityNodeReport(
                    node.Id,
                    node.GetType().Name,
                    resolveName(node),
                    reasons);
            })
            .ToArray();

        return new NodeInvalidityReport(
            grouped,
            analysis.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Error),
            analysis.Diagnostics.Count(static d => d.Severity == DiagnosticSeverity.Warning));
    }
}