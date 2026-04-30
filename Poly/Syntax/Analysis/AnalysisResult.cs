namespace Poly.Syntax.Analysis;

public sealed record AnalysisResult : INodeMetadataProvider {
    private readonly NodeMetadataStore _metadata;
    private readonly Dictionary<NodeId, List<Diagnostic>> _diagnostics;
    private readonly Lazy<IReadOnlyList<Diagnostic>> _allDiagnostics;

    public AnalysisResult(AnalysisContext context) {
        ArgumentNullException.ThrowIfNull(context);
        _metadata = context.Metadata;
        _diagnostics = context.Diagnostics;
        _allDiagnostics = new Lazy<IReadOnlyList<Diagnostic>>(() => _diagnostics.SelectMany(kvp => kvp.Value).ToList());
    }

    /// <summary>
    /// Gets the collection of diagnostics produced during analysis.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _allDiagnostics.Value;

    /// <summary>
    /// Returns true if any error-level diagnostics were produced.
    /// </summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets metadata of the specified type for the given node.
    /// Returns null if no metadata of that type exists for the node.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <param name="node">The node for which to retrieve metadata.</param>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata => _metadata.Get<TMetadata>(node);

    internal NodeMetadataStore GetMetadataStore() => _metadata;
    internal Dictionary<NodeId, List<Diagnostic>> GetDiagnosticsDictionary() => _diagnostics;
}