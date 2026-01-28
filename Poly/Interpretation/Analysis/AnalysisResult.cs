namespace Poly.Interpretation.Analysis;

public sealed record AnalysisResult : ITypedMetadataProvider {
    private readonly TypedMetadataStore _metadata;

    public AnalysisResult(TypedMetadataStore metadata, IReadOnlyList<Diagnostic>? diagnostics = null)
    {
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

    public IEnumerable<object> Metadata => _metadata.GetAll();

    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => _metadata.Get<TMetadata>();
}