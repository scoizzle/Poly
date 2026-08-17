using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Mutable assembly surface for <see cref="IDomainLibrary.Register"/>. Libraries
/// contribute meaning, type maps, conventions, and artifacts; <see cref="Build"/>
/// freezes the result into a <see cref="DomainSession"/>. The session is the
/// primary assembler — this builder is the library-registration surface, not a
/// fail-open door.
/// </summary>
public sealed class SessionBuilder {
    private readonly List<IStorageConvention> _storageConventions = [];
    private readonly List<IArtifactContributor> _artifacts = [];
    private readonly List<string> _loadedIds = [];
    private readonly HashSet<string> _loadedIdSet = new(StringComparer.Ordinal);

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();
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
            DomainSession.FoldsFor(language.Grammar, expressionForms),
            Meaning,
            _artifacts);
    }
}