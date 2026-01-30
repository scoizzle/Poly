namespace Poly.Interpretation.Analysis;

/// <summary>
/// Provides context for analysis operations, including type definitions and metadata storage.
/// </summary>
public sealed class AnalysisContext : ITypedMetadataProvider {
    private readonly List<Diagnostic> _diagnostics = new();

    /// <summary>
    /// Initializes a new instance with type definitions.
    /// </summary>
    public AnalysisContext(ITypeDefinitionProvider typeDefinitions)
    {
        TypeDefinitions = typeDefinitions;
        Metadata = new NodeMetadataStore();
    }

    /// <summary>
    /// Initializes a new instance with type definitions and pre-populated metadata from a previous analysis.
    /// </summary>
    public AnalysisContext(ITypeDefinitionProvider typeDefinitions, NodeMetadataStore previousMetadata)
    {
        TypeDefinitions = typeDefinitions;
        Metadata = new NodeMetadataStore(previousMetadata);
    }

    /// <summary>
    /// Gets the metadata store for associating arbitrary data with AST nodes during analysis.
    /// </summary>
    public NodeMetadataStore Metadata { get; }

    /// <summary>
    /// Gets the type definition provider used for resolving type information.
    /// </summary>
    public ITypeDefinitionProvider TypeDefinitions { get; }

    /// <summary>
    /// Gets the diagnostics collected during analysis.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Reports a diagnostic for the specified node.
    /// </summary>
    public void ReportDiagnostic(Node node, DiagnosticSeverity severity, string message, string? code = null) => _diagnostics.Add(new Diagnostic(node, severity, message, code));

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
}