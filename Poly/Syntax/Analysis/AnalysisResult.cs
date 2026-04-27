namespace Poly.Syntax.Analysis;

public sealed record AnalysisResult : INodeMetadataProvider {
    private readonly NodeMetadataStore _metadata;

    public AnalysisResult(NodeMetadataStore metadata, IReadOnlyList<Diagnostic>? diagnostics = null) {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
        Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
    }

    /// <summary>
    /// Gets the collection of diagnostics produced during analysis.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; }

    /// <summary>
    /// Returns true if any error-level diagnostics were produced.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public TMetadata? GetMetadata<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata => _metadata.Get<TMetadata>(node);

    public IEnumerable<IAnalysisMetadata> GetAllMetadata(Node node) => _metadata.GetAll(node);
}
