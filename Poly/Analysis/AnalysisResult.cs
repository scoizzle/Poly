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
    /// <summary>
    /// The type provider of this analysis: its AST type table over the caller's
    /// providers; a name it cannot answer is its type system's target chain's (#998 T12a). Belongs to one analysis, so concurrent or
    /// sequential analyses never answer for each other.
    /// </summary>
    public ITypeDefinitionProvider TypeDefinitions { get; init; } =
        Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;

    /// <summary>The type system the analysis decided by, which emit asks too: this analysis' own layer over its target
    /// (#998 T12a, T4c). It is the same instance the analysis used, never recomputed from settings.</summary>
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