using Poly.Analysis;
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
    private readonly List<(string LibraryId, string Name, TypeCategory Category)> _primitiveSeeds = [];
    private readonly List<IArtifactContributor> _artifacts = [];
    private readonly List<INodeAnalyzer> _analyzers = [];
    private readonly HashSet<string> _analyzerPassNames = new(StringComparer.Ordinal);
    private readonly List<MethodContribution> _methodContributions = [];
    private readonly HashSet<(string LibraryId, string TypeName, string MethodName)> _contributionKeys = [];
    private readonly List<string> _loadedIds = [];
    private readonly HashSet<string> _loadedIdSet = new(StringComparer.Ordinal);

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();
    public IReadOnlyList<IStorageConvention> StorageConventions => _storageConventions;
    public ExpressionMeaning Meaning { get; } = new();

    /// <summary>No extensions loaded.</summary>
    public static SessionBuilder CreateEmpty() => new();

    /// <summary>
    /// Loads <paramref name="library"/>. Duplicate <see cref="IDomainLibrary.Id"/> fails closed.
    /// </summary>
    public SessionBuilder Load(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.Id))
            throw new ArgumentException("Library id must be non-empty.", nameof(library));
        if (!_loadedIdSet.Add(library.Id))
            throw new InvalidOperationException($"A library with id '{library.Id}' is already loaded.");
        library.Register(this);
        _loadedIds.Add(library.Id);
        foreach (var (name, category) in library.PrimitiveSeeds)
            _primitiveSeeds.Add((library.Id, name, category));
        return this;
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
    /// Appends a library analyzer after the core list. Duplicate
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
        return this;
    }

    /// <summary>
    /// Records a stub or statement prefix a library wants on a produced method,
    /// plus the parameters that stub requires. Core threads
    /// <see cref="ContributedParameter.ThreadToCallers"/> parameters through
    /// callers after lowering. The library does not name those callers.
    /// </summary>
    public SessionBuilder AddMethodContribution(MethodContribution contribution) {
        ArgumentNullException.ThrowIfNull(contribution);
        if (string.IsNullOrWhiteSpace(contribution.LibraryId))
            throw new ArgumentException("Library id must be non-empty.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.TargetTypeName))
            throw new ArgumentException("Target type name must be non-empty.", nameof(contribution));
        if (string.IsNullOrWhiteSpace(contribution.MethodName))
            throw new ArgumentException("Method name must be non-empty.", nameof(contribution));
        if (contribution.NewMethod is null
            && (contribution.PrependStatements is null || contribution.PrependStatements.Count == 0)
            && contribution.Parameters.Count == 0)
            throw new ArgumentException(
                "A method contribution must add a method, statements, or parameters.",
                nameof(contribution));
        if (contribution.NewMethod is not null
            && !string.Equals(contribution.NewMethod.Name, contribution.MethodName, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Contribution method name '{contribution.MethodName}' does not match '{contribution.NewMethod.Name}'.",
                nameof(contribution));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in contribution.Parameters) {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                throw new ArgumentException("Contributed parameter name must be non-empty.", nameof(contribution));
            if (parameter.Type is null)
                throw new ArgumentException(
                    $"Contributed parameter '{parameter.Name}' requires a type.", nameof(contribution));
            if (!names.Add(parameter.Name))
                throw new InvalidOperationException(
                    $"Library '{contribution.LibraryId}' repeats parameter '{parameter.Name}' on {contribution.TargetTypeName}.{contribution.MethodName}.");
        }

        if (!_contributionKeys.Add((contribution.LibraryId, contribution.TargetTypeName, contribution.MethodName)))
            throw new InvalidOperationException(
                $"Library '{contribution.LibraryId}' already contributed {contribution.TargetTypeName}.{contribution.MethodName}.");
        _methodContributions.Add(contribution);
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
            _primitiveSeeds,
            _methodContributions.ToArray());
    }
}