using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Sessions;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Fail-closed tests strip bags by copying the result store, never by mutating
/// the <see cref="AnalysisResult"/> produced by analyze.
/// </summary>
internal static class AnalysisResultMetadata {
    public static AnalysisResult WithoutMetadata<T>(this AnalysisResult analysis, Node? node)
        where T : class, IAnalysisMetadata {
        if (analysis.Metadata is not NodeMetadataStore source)
            throw new InvalidOperationException("Analysis metadata is not a NodeMetadataStore.");
        var copy = new NodeMetadataStore(source);
        copy.Remove<T>(node);
        return analysis with { Metadata = copy };
    }

    public static AnalysisResult RebindWithoutMetadata<T>(this AnalysisResult analysis, Domain domain, Node? node)
        where T : class, IAnalysisMetadata {
        var stripped = analysis.WithoutMetadata<T>(node);
        RuntimeAnalysisCache.Bind(domain, RuntimeAnalysisCache.Session(domain), stripped);
        return stripped;
    }

    public static void ReplaceSessionAnalysis(string sessionId, AnalysisResult analysis) {
        if (!McpSessionStore.TryGet(sessionId, out var state))
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        McpSessionStore.Replace(sessionId, state.Domain, analysis);
    }
}