namespace Poly.Syntax.Analysis;

/// <summary>
/// Provides context for analysis operations, including type definitions and metadata storage.
/// </summary>
public sealed class AnalysisContext : INodeMetadataProvider {
    /// <summary>
    /// Initializes a new instance with type definitions.
    /// </summary>
    public AnalysisContext(ITypeDefinitionProvider typeDefinitions) {
        TypeDefinitions = typeDefinitions;
        Metadata = new NodeMetadataStore();
        Diagnostics = new Dictionary<NodeId, List<Diagnostic>>();
    }

    public AnalysisContext(ITypeDefinitionProvider typeDefinitions, AnalysisResult priorAnalysis) {
        ArgumentNullException.ThrowIfNull(priorAnalysis);

        TypeDefinitions = typeDefinitions;
        Metadata = new NodeMetadataStore(priorAnalysis.GetMetadataStore());
        Diagnostics = priorAnalysis.GetDiagnosticsDictionary();
    }

    /// <summary>
    /// Gets the metadata store for associating arbitrary data with AST nodes during analysis.
    /// </summary>
    public NodeMetadataStore Metadata { get; }

    /// <summary>
    /// Gets the diagnostics collected during analysis, keyed by node identifier.
    /// </summary>
    public Dictionary<NodeId, List<Diagnostic>> Diagnostics { get; }

    /// <summary>
    /// Gets the type definition provider used for resolving type information.
    /// </summary>
    public ITypeDefinitionProvider TypeDefinitions { get; }

    /// <summary>
    /// Reports a diagnostic for the specified node.
    /// </summary>
    public void ReportDiagnostic(Node node, DiagnosticSeverity severity, string message, string? code = null) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(message);

        if (!Diagnostics.TryGetValue(node.Id, out var bucket)) {
            bucket = new List<Diagnostic>();
            Diagnostics[node.Id] = bucket;
        }

        bucket.Add(new Diagnostic(node, severity, message, code));
    }

    /// <summary>
    /// Reports an error diagnostic for the specified node.
    /// </summary> <param name="node">The node associated with the error.</param>
    /// <param name="message">The error message.</param>
    /// <param name="code">An optional error code.</param>
    public IReadOnlyList<Diagnostic> GetDiagnostics(NodeId nodeId) =>
        Diagnostics.TryGetValue(nodeId, out var diagnostics) ? diagnostics : [];


    /// <summary>
    /// Clears diagnostics for the specified node id.
    /// </summary>
    /// <param name="nodeId">The node identifier for which to clear diagnostics.</param>
    /// <returns>True if diagnostics were cleared; false if no diagnostics existed for the node id.</returns>
    public bool ClearDiagnostics(NodeId nodeId) => Diagnostics.Remove(nodeId, out var bucket);

    /// <summary>
    /// Gets metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node node) where TMetadata : class, IAnalysisMetadata => Metadata.Get<TMetadata>(node);

    /// <summary>
    /// Gets or adds metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to get or add.</typeparam>
    /// <param name="factory">A factory function to create the metadata if it does not exist.</param>
    /// <returns>The existing or newly added metadata of the specified type.</returns>
    public TMetadata GetOrAddMetadata<TMetadata>(Node node, Func<TMetadata> factory) where TMetadata : class, IAnalysisMetadata => Metadata.GetOrAdd(node, factory);

    /// <summary>
    /// Sets metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to set.</typeparam>
    /// <param name="metadata">The metadata instance to set.</param>
    public void SetMetadata<TMetadata>(Node node, TMetadata metadata) where TMetadata : class, IAnalysisMetadata => Metadata.Set(node, metadata);

    /// <summary>
    /// Removes metadata of the specified type.
    /// </summary>
    /// <param name="node">The node for which to clear metadata.</param>
    public void ClearMetadata(Node node) => Metadata.RemoveAll(node);

    /// <summary>
    /// Removes metadata for the specified node id.
    /// </summary>
    /// <param name="nodeId">The node identifier for which to clear metadata.</param>
    public void ClearMetadata(NodeId nodeId) => Metadata.RemoveAll(nodeId);
}