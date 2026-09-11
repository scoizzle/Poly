namespace Poly.Analysis;

/// <param name="HasErrors">True when any error-level diagnostic was reported; taken from the analysis context flag at construction (not re-scanned from <paramref name="Diagnostics"/>).</param>
public sealed record AnalysisResult(
    INodeMetadataProvider Metadata,
    AnalysisTelemetry Telemetry,
    IReadOnlyList<Diagnostic> Diagnostics,
    AnalysisSettings Settings,
    AnalysisOptions? Options = null,
    bool HasErrors = false
) : INodeMetadataProvider {
    /// <summary>
    /// Gets metadata of the specified type for the given node.
    /// Returns null if no metadata of that type exists for the node.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <param name="node">The node for which to retrieve metadata.</param>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata => Metadata.GetMetadata<TMetadata>(node);
}
