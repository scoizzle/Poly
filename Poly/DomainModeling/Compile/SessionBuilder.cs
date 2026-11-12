using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Mutable assembly surface for <see cref="IDomainLibrary.Register"/>. Libraries
/// contribute analyzers (the product extension slot), plus type maps / conventions
/// those passes close over. <see cref="Build"/> freezes the result into a
/// <see cref="DomainSession"/>.
/// </summary>
public sealed class SessionBuilder {
    private readonly List<IStorageConvention> _storageConventions = [];
    private readonly List<(string LibraryId, string Name, PrimitiveSemantics Semantics)> _primitiveSeeds = [];
    private readonly List<IArtifactContributor> _artifacts = [];
    private readonly List<INodeAnalyzer> _analyzers = [];
    private readonly HashSet<string> _analyzerPassNames = new(StringComparer.Ordinal);
    private readonly List<IConstraintCompiler> _constraintCompilers = [];
    private readonly List<string> _loadedIds = [];
    private readonly HashSet<string> _loadedIdSet = new(StringComparer.Ordinal);
    private readonly List<StageContribution> _contributions = [];
    private readonly HashSet<(PipelineStage Stage, string LibraryId)> _contributionKeys = [];
    private readonly List<ContractContribution> _contracts = [];
    private string? _registeringLibraryId;
    private IReadOnlyList<ObservedFlow> _observedFlows = [];
    private IReadOnlyList<DiagnosticAllowance> _diagnosticAllowances = [];

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();
    public IReadOnlyList<IStorageConvention> StorageConventions => _storageConventions;
    public IReadOnlyList<StageContribution> Contributions => _contributions;
    public IReadOnlyList<ContractContribution> Contracts => _contracts;
    public ExpressionMeaning Meaning { get; } = new();

    /// <summary>Copies the platform's reader found already bound by <see cref="WithObservedFlows"/>.</summary>
    public IReadOnlyList<ObservedFlow> ObservedFlows => _observedFlows;

    /// <summary>Allowances bound by <see cref="WithDiagnosticAllowances"/>.</summary>
    public IReadOnlyList<DiagnosticAllowance> DiagnosticAllowances => _diagnosticAllowances;

    /// <summary>
    /// Binds the copies the platform's reader found. Analysis checks each one with the assign
    /// checks. A null element or a repeated source and target pair throws.
    /// </summary>
    public SessionBuilder WithObservedFlows(IReadOnlyList<ObservedFlow>? flows) {
        if (flows is not null && flows.Any(flow => flow is null))
            throw new ArgumentException("Observed flows must not contain null.", nameof(flows));
        var copy = flows is null ? [] : flows.ToArray();
        var seen = new HashSet<ObservedFlow>();
        foreach (var flow in copy) {
            if (!seen.Add(flow))
                throw new ArgumentException(
                    $"Observed flow '{flow.SourcePath}' -> '{flow.TargetPath}' is bound more than once.", nameof(flows));
        }
        _observedFlows = copy;
        return this;
    }

    /// <summary>
    /// Binds the acknowledgements of the diagnostics those flow checks emit. A null element throws.
    /// Whether an allowance matches is decided at analysis, not here.
    /// </summary>
    public SessionBuilder WithDiagnosticAllowances(IReadOnlyList<DiagnosticAllowance>? allowances) {
        if (allowances is not null && allowances.Any(allowance => allowance is null))
            throw new ArgumentException("Diagnostic allowances must not contain null.", nameof(allowances));
        _diagnosticAllowances = allowances is null ? [] : allowances.ToArray();
        return this;
    }

    /// <summary>
    /// Records one library's entry into <paramref name="stage"/>, once per stage and library.
    /// Load, analyzer, and constraint-compiler registration are the write sites. Analyzer and
    /// compiler registration attribute themselves to the library whose
    /// <see cref="IDomainLibrary.Register"/> is running; a call made outside <see cref="Load"/>
    /// or <see cref="LoadCore"/> (for example a storage facet library calling its register
    /// method directly) has no registering library and records no contribution.
    /// </summary>
    internal SessionBuilder AddContribution(PipelineStage stage, string libraryId) {
        if (string.IsNullOrWhiteSpace(libraryId))
            throw new ArgumentException("Library id must be non-empty.", nameof(libraryId));
        if (_contributionKeys.Add((stage, libraryId)))
            _contributions.Add(new StageContribution(stage, libraryId));
        return this;
    }

    /// <summary>No extensions loaded.</summary>
    public static SessionBuilder CreateEmpty() => new();

    /// <summary>
    /// Loads <paramref name="library"/> as an authored extension. Duplicate
    /// <see cref="IDomainLibrary.Id"/> fails closed.
    /// </summary>
    public SessionBuilder Load(IDomainLibrary library) {
        Register(library);
        _loadedIds.Add(library.Id);
        foreach (var (name, semantics) in library.PrimitiveSeeds)
            _primitiveSeeds.Add((library.Id, name, semantics));
        return this;
    }

    /// <summary>
    /// Loads <see cref="CoreLibrary"/> at <see cref="PipelineStage.Load"/>. Core is
    /// implicit, so it is not recorded in the session's authored extension ids.
    /// </summary>
    public SessionBuilder LoadCore() {
        Register(new CoreLibrary());
        return this;
    }

    /// <summary>
    /// Loads <see cref="TemporalLibrary"/> at <see cref="PipelineStage.Load"/>
    /// without recording <c>temporal</c> as an authored extension id.
    /// </summary>
    public SessionBuilder LoadTemporal() {
        var library = new TemporalLibrary();
        Register(library);
        foreach (var (name, semantics) in library.PrimitiveSeeds)
            _primitiveSeeds.Add((library.Id, name, semantics));
        return this;
    }

    private void Register(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.Id))
            throw new ArgumentException("Library id must be non-empty.", nameof(library));
        if (!_loadedIdSet.Add(library.Id))
            throw new InvalidOperationException($"A library with id '{library.Id}' is already loaded.");
        AddContribution(PipelineStage.Load, library.Id);
        var previousLibraryId = _registeringLibraryId;
        _registeringLibraryId = library.Id;
        try {
            library.Register(this);
        }
        finally {
            _registeringLibraryId = previousLibraryId;
        }
    }

    public SessionBuilder AddStorageConvention(IStorageConvention convention) {
        ArgumentNullException.ThrowIfNull(convention);
        _storageConventions.Add(convention);
        return this;
    }

    public SessionBuilder AddArtifactContributor(IArtifactContributor contributor) {
        ArgumentNullException.ThrowIfNull(contributor);
        _artifacts.Add(contributor);
        return this;
    }

    /// <summary>
    /// Appends an analyzer to the pipeline. Duplicate
    /// <see cref="INodeAnalyzer.PassName"/> fails closed. Libraries do not splice
    /// the middle of the pipeline.
    /// </summary>
    public SessionBuilder AddAnalyzer(INodeAnalyzer analyzer) {
        ArgumentNullException.ThrowIfNull(analyzer);
        if (string.IsNullOrWhiteSpace(analyzer.PassName))
            throw new ArgumentException("Analyzer PassName must be non-empty.", nameof(analyzer));
        if (!_analyzerPassNames.Add(analyzer.PassName))
            throw new InvalidOperationException(
                $"An analyzer with pass name '{analyzer.PassName}' is already registered.");
        _analyzers.Add(analyzer);
        if (_registeringLibraryId is not null)
            AddContribution(PipelineStage.Analyze, _registeringLibraryId);
        return this;
    }

    /// <summary>
    /// Records a compiler for constraints that produce AST and implied
    /// parameters. Two compilers from the same library and CLR type fail closed.
    /// Which constraint they own is decided by <see cref="IConstraintCompiler.Applies"/>.
    /// </summary>
    public SessionBuilder AddConstraintCompiler(IConstraintCompiler compiler) {
        ArgumentNullException.ThrowIfNull(compiler);
        if (string.IsNullOrWhiteSpace(compiler.LibraryId))
            throw new ArgumentException("Library id must be non-empty.", nameof(compiler));
        if (_constraintCompilers.Any(existing =>
                string.Equals(existing.LibraryId, compiler.LibraryId, StringComparison.Ordinal)
                && existing.GetType() == compiler.GetType()))
            throw new InvalidOperationException(
                $"Library '{compiler.LibraryId}' already registered constraint compiler '{compiler.GetType().Name}'.");
        _constraintCompilers.Add(compiler);
        if (_registeringLibraryId is not null)
            AddContribution(PipelineStage.Lower, _registeringLibraryId);
        return this;
    }

    /// <summary>
    /// Contributes one library's contract surface and, optionally, the AST implementing each
    /// endpoint. Only a running <see cref="IDomainLibrary.Register"/> may call this: the
    /// contribution is attributed to that library, and a second contributor for the same
    /// contract name fails closed (one contributor per surface).
    /// </summary>
    /// <param name="surface">The exported surface. Its version is the package's, never a caller pin.</param>
    /// <param name="bodies">
    /// Endpoint name to a factory that builds the implementation body. Lower calls it once per
    /// splice site, so the factory must return a fresh <see cref="Block"/> with fresh node ids each time.
    /// Never run at Load.
    /// </param>
    /// <param name="targets">
    /// Host targets this package is for (<c>posix</c>, <c>windows</c>). Empty declares no
    /// platform constraint. Analysis compares this with the host's target; <c>uses</c> never
    /// chooses or implies one.
    /// </param>
    public SessionBuilder AddContract(
        ImportedContract surface,
        IReadOnlyDictionary<string, Func<Block>>? bodies = null,
        IReadOnlyList<string>? targets = null) {
        ArgumentNullException.ThrowIfNull(surface);
        if (_registeringLibraryId is null)
            throw new InvalidOperationException(
                "A contract contribution is made from IDomainLibrary.Register so it can be attributed to its library.");
        if (string.IsNullOrWhiteSpace(surface.Name))
            throw new ArgumentException("Contract name must be non-empty.", nameof(surface));

        var existing = _contracts.FirstOrDefault(contribution =>
            string.Equals(contribution.Surface.Name, surface.Name, StringComparison.Ordinal));
        if (existing is not null)
            throw new InvalidOperationException(
                $"Contract '{surface.Name}' is already contributed by library '{existing.LibraryId}'. One contributor.");

        var duplicateEndpoint = surface.Endpoints
            .GroupBy(endpoint => endpoint.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateEndpoint is not null)
            throw new InvalidOperationException(
                $"Contract '{surface.Name}' declares endpoint '{duplicateEndpoint.Key}' more than once.");

        var bodyMap = bodies ?? new Dictionary<string, Func<Block>>(StringComparer.Ordinal);
        foreach (var (endpointName, body) in bodyMap) {
            if (body is null)
                throw new ArgumentException(
                    $"Contract '{surface.Name}' body for endpoint '{endpointName}' must not be null.", nameof(bodies));
            if (!surface.Endpoints.Any(endpoint =>
                    string.Equals(endpoint.Name, endpointName, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Contract '{surface.Name}' has no endpoint '{endpointName}' for the contributed body.");
        }

        var targetIds = targets ?? [];
        foreach (var target in targetIds) {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException(
                    $"Contract '{surface.Name}' declares a blank host target.", nameof(targets));
        }
        if (targetIds.Distinct(StringComparer.Ordinal).Count() != targetIds.Count)
            throw new ArgumentException(
                $"Contract '{surface.Name}' declares a host target more than once.", nameof(targets));

        _contracts.Add(new ContractContribution(_registeringLibraryId, surface, bodyMap) {
            Targets = [.. targetIds],
        });
        return this;
    }

    /// <summary>Freezes the loaded libraries into a session.</summary>
    public DomainSession Build(Domain? domain = null) =>
        Build(SessionOptions.Bare(ExtensionCatalog.Core), domain);

    /// <summary>Freezes the loaded libraries into a session that remembers <paramref name="options"/>.</summary>
    internal DomainSession Build(SessionOptions options, Domain? domain = null) {
        ArgumentNullException.ThrowIfNull(options);
        var annotations = new AnnotationRegistry(Annotations);
        var expressionForms = new ExpressionFormRegistry(ExpressionForms);
        var language = DslGrammar.LanguageFor(annotations, expressionForms);
        return new DomainSession(
            domain,
            _loadedIds.ToArray(),
            options,
            language,
            annotations,
            expressionForms,
            TypeMaps.Clone(),
            _storageConventions,
            DomainSession.FoldsFor(expressionForms),
            Meaning,
            _artifacts,
            _analyzers,
            _contributions.ToArray(),
            _primitiveSeeds,
            _constraintCompilers.ToArray(),
            _observedFlows,
            _diagnosticAllowances,
            _contracts.ToArray());
    }
}