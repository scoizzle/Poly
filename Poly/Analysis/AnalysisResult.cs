namespace Poly.Analysis;

using Poly.Introspection;

/// <param name="HasErrors">True when any error-level diagnostic was reported; taken from the analysis context flag at construction (not re-scanned from <paramref name="Diagnostics"/>).</param>
public sealed record AnalysisResult(
    INodeMetadataProvider Metadata,
    AnalysisTelemetry Telemetry,
    IReadOnlyList<Diagnostic> Diagnostics,
    AnalysisSettings Settings,
    AnalysisOptions? Options = null,
    bool HasErrors = false
) : INodeMetadataProvider {
    /// <summary>The type system the analysis decided by, which emit asks too: this analysis' own layer over its target
    /// (#998 T12a, T4c). It is the same instance the analysis used, never recomputed from settings. Its
    /// <see cref="ITypeSystem.Resolve"/> answers the analysis' names, and its own sources hold what the analysis
    /// declared (#998 T12b).</summary>
    public ITypeSystem TypeSystem { get; init; } = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;

    /// <summary>
    /// Gets metadata of the specified type for the given node.
    /// Returns null if no metadata of that type exists for the node.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <param name="node">The node for which to retrieve metadata.</param>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata => Metadata.GetMetadata<TMetadata>(node);
}