namespace Poly.Analysis;

using Poly.Introspection;

/// <summary>
/// Provides context for analysis operations, including type definitions and metadata storage.
/// </summary>
public sealed class AnalysisContext : INodeMetadataProvider {
    private readonly List<Diagnostic> _diagnostics = [];

    public static AnalysisContext CreateDefault() => new();

    /// <summary>The type system the analysis decides by: this analysis' own layer over the <see cref="ITypeSystem"/>
    /// setting, else over C# (#998 T12a, T12b). Its own sources answer a name first; its <see cref="ITypeSystem.Target"/>
    /// answers the rest. Every analysis-time operator lookup (#998 T4a), conversion and assignability check, and overload
    /// ranking (T4b) asks it.</summary>
    public ITypeSystem TypeSystem => Layer;

    /// <summary>The layer behind <see cref="TypeSystem"/>, where the analysis registers what it declares.</summary>
    internal AnalysisTypeSystem Layer { get; }

    public AnalysisContext() : this(AnalysisSettings.Default) { }

    /// <summary>
    /// Initializes a new instance with the given settings: the target type system is the <see cref="ITypeSystem"/> setting,
    /// else C# over the CLR (#998 T12a, T12b).
    /// </summary>
    public AnalysisContext(AnalysisSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        Layer = new AnalysisTypeSystem(settings.Get<ITypeSystem>() ?? Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
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
    /// Registers a source the analysis owns, outranking the ones already registered: a source a caller supplied, or the
    /// declarations the analysis found (#998 T12b). The layer's name lookup answers it before the target chain.
    /// </summary>
    internal void AddSource(ITypeSource source) => Layer.AddSource(source);

    /// <summary>Registers a block of own sources in the block's order.</summary>
    internal void AddSources(IEnumerable<ITypeSource> sources) => Layer.AddSources(sources);

    /// <summary>Registers a source beneath every other own source, above the target: a unit's <c>using</c> scope.</summary>
    internal void AddScopeSource(ITypeSource source) => Layer.AddScopeSource(source);

    /// <summary>The definition built for exactly <paramref name="node"/> by one of the own sources, or null.</summary>
    internal ITypeDefinition? TypeDefinitionOf(TypeDefinitionNode node) => Layer.DefinitionOf(node);

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