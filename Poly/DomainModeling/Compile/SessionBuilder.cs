using Poly.Analysis;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;

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
    private string? _registeringLibraryId;

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();
    public IReadOnlyList<IStorageConvention> StorageConventions => _storageConventions;
    public IReadOnlyList<StageContribution> Contributions => _contributions;
    public ExpressionMeaning Meaning { get; } = new();

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

    /// <summary>Freezes the loaded libraries into a session.</summary>
    public DomainSession Build(Domain? domain = null) {
        var annotations = new AnnotationRegistry(Annotations);
        var expressionForms = new ExpressionFormRegistry(ExpressionForms);
        var language = DslGrammar.LanguageFor(annotations, expressionForms);
        return new DomainSession(
            domain,
            _loadedIds.ToArray(),
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
            _constraintCompilers.ToArray());
    }
}