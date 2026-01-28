namespace Poly.Interpretation.Analysis;

/// <summary>
/// Provides context for analysis operations, including type definitions and metadata storage.
/// </summary>
/// <param name="typeDefinitions">The type definition provider for resolving type information.</param>
public sealed class AnalysisContext(ITypeDefinitionProvider typeDefinitions) : ITypedMetadataProvider {
    private readonly List<Diagnostic> _diagnostics = new();

    /// <summary>
    /// Gets the metadata store for associating arbitrary data with AST nodes during analysis.
    /// </summary>
    public TypedMetadataStore Metadata { get; } = new();

    /// <summary>
    /// Gets the type definition provider used for resolving type information.
    /// </summary>
    public ITypeDefinitionProvider TypeDefinitions { get; } = typeDefinitions;

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
    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class => Metadata.Get<TMetadata>();

    /// <summary>
    /// Gets or adds metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to get or add.</typeparam>
    /// <param name="factory">A factory function to create the metadata if it does not exist.</param>
    /// <returns>The existing or newly added metadata of the specified type.</returns>
    public TMetadata GetOrAddMetadata<TMetadata>(Func<TMetadata> factory) where TMetadata : class => Metadata.GetOrAdd(factory);

    /// <summary>
    /// Sets metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to set.</typeparam>
    /// <param name="metadata">The metadata instance to set.</param>
    public void SetMetadata<TMetadata>(TMetadata metadata) where TMetadata : class => Metadata.Set(metadata);
}