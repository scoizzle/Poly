namespace Poly.Analysis;

using Poly.Introspection;

/// <summary>
/// Provides context for analysis operations, including type definitions and metadata storage.
/// </summary>
public sealed class AnalysisContext : INodeMetadataProvider {
    private readonly List<Diagnostic> _diagnostics = [];

    public static AnalysisContext CreateDefault() => new();

    public AnalysisContext() : this(Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared, AnalysisSettings.Default) { }

    public AnalysisContext(ITypeDefinitionProvider typeDefinitions)
        : this(typeDefinitions, AnalysisSettings.Default) { }

    /// <summary>
    /// Initializes a new instance with type definitions.
    /// </summary>
    public AnalysisContext(ITypeDefinitionProvider typeDefinitions, AnalysisSettings settings) {
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(settings);
        var clr = Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared;
        if (typeDefinitions is TypeDefinitionProviderCollection tpc) {
            TypeDefinitions = tpc;
            tpc.AddFallback(clr);
        }
        else if (ReferenceEquals(typeDefinitions, clr)) {
            TypeDefinitions = new TypeDefinitionProviderCollection(clr);
        }
        else {
            TypeDefinitions = new TypeDefinitionProviderCollection(typeDefinitions, clr);
        }
        Settings = settings;
        AnalysisDiagnosticConfiguration = Settings.Get<AnalysisDiagnosticConfiguration>() ?? AnalysisDiagnosticConfiguration.Default;
    }

    /// <summary>
    /// Gets run-level settings for this analysis execution.
    /// </summary>
    public AnalysisSettings Settings { get; }

    /// <summary>
    /// Gets the metadata store for associating arbitrary data with AST nodes during analysis.
    /// </summary>
    public NodeMetadataStore Metadata { get; } = new();

    /// <summary>
    /// Diagnostics reported during this analysis, in report order.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Gets the diagnostic configuration used for this analysis run, which controls severity normalization and filtering.
    /// </summary>
    public AnalysisDiagnosticConfiguration AnalysisDiagnosticConfiguration { get; }

    /// <summary>
    /// Gets the type definition provider used for resolving type information.
    /// This is always a <see cref="TypeDefinitionProviderCollection"/> — providers
    /// registered during analysis (e.g. AST type definitions from
    /// <c>TypeDefinitionNodeAnalyzer</c>) are added to this collection.
    /// </summary>
    public TypeDefinitionProviderCollection TypeDefinitions { get; }

    /// <summary>
    /// True when any error-level diagnostic has been reported.
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Reports a diagnostic for the specified node.
    /// </summary>
    public void ReportDiagnostic(Node node, DiagnosticSeverity severity, string message, string? code = null) {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(message);

        severity = AnalysisDiagnosticConfiguration.NormalizeSeverity(severity);

        if (!AnalysisDiagnosticConfiguration.ShouldInclude(severity))
            return;

        _diagnostics.Add(new Diagnostic(node, severity, message, code));

        if (severity == DiagnosticSeverity.Error)
            HasErrors = true;
    }

    /// <summary>
    /// Gets metadata of the specified type.
    /// </summary>
    /// <typeparam name="TMetadata">The type of metadata to retrieve.</typeparam>
    /// <returns>The metadata of the specified type, or null if not found.</returns>
    public TMetadata? GetMetadata<TMetadata>(Node? node) where TMetadata : class, IAnalysisMetadata => Metadata.Get<TMetadata>(node);

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
    public void SetMetadata<TMetadata>(Node? node, TMetadata metadata) where TMetadata : class, IAnalysisMetadata => Metadata.Set(node, metadata);

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