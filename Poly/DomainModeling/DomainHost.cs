using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling;

/// <summary>
/// Frozen parse/print and analysis tables for a set of extension ids.
/// Product callers open a <see cref="DomainSession"/>; the catalog builds this bundle.
/// </summary>
public sealed record DomainHost(
    DomainParserInputs Parser,
    DomainAnalysisInputs Analysis,
    ExpressionMeaning Meaning
) {
    public IReadOnlyList<IArtifactContributor> Artifacts { get; init; } = [];
}

/// <summary>
/// Immutable parser/printer tables for one session.
/// </summary>
public sealed class DomainParserInputs {
    /// <summary>Empty tables — no libraries loaded.</summary>
    public static DomainParserInputs Empty { get; } = new(new AnnotationRegistry());

    public AnnotationRegistry Annotations { get; }

    /// <summary>Concept folds and print mappings on product expression shapes.</summary>
    public ExpressionFormRegistry ExpressionForms { get; }

    public DomainParserInputs(AnnotationRegistry annotations, ExpressionFormRegistry? expressionForms = null) {
        ArgumentNullException.ThrowIfNull(annotations);
        Annotations = new AnnotationRegistry(annotations);
        ExpressionForms = expressionForms is null
            ? new ExpressionFormRegistry()
            : new ExpressionFormRegistry(expressionForms);
    }
}

/// <summary>
/// Immutable analyzer tables for one session.
/// </summary>
public sealed class DomainAnalysisInputs {
    /// <summary>Empty type maps and storage conventions.</summary>
    public static DomainAnalysisInputs Empty { get; } = new(
        new TypeMappingRegistry(),
        []);

    public TypeMappingRegistry TypeMaps { get; }
    public IReadOnlyList<IStorageConvention> StorageConventions { get; }

    public DomainAnalysisInputs(
        TypeMappingRegistry typeMaps,
        IReadOnlyList<IStorageConvention> storageConventions) {
        ArgumentNullException.ThrowIfNull(typeMaps);
        ArgumentNullException.ThrowIfNull(storageConventions);
        TypeMaps = typeMaps.Clone();
        StorageConventions = storageConventions.ToArray();
    }
}

/// <summary>
/// Composes a <see cref="DomainHost"/> by loading libraries into parse and analysis surfaces.
/// </summary>
public sealed class DomainHostBuilder {
    private readonly List<IStorageConvention> _storageConventions = [];
    private readonly List<IArtifactContributor> _artifacts = [];
    private readonly HashSet<string> _loadedIds = new(StringComparer.Ordinal);

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();
    public ExpressionMeaning Meaning { get; } = new();

    /// <summary>No extensions loaded — used by the catalog when resolving ids.</summary>
    public static DomainHostBuilder CreateEmpty() => new();

    /// <summary>
    /// Loads <paramref name="library"/>. Duplicate <see cref="IDomainLibrary.Id"/> fails closed.
    /// </summary>
    public DomainHostBuilder Load(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.Id))
            throw new ArgumentException("Library id must be non-empty.", nameof(library));
        if (!_loadedIds.Add(library.Id))
            throw new InvalidOperationException($"A library with id '{library.Id}' is already loaded.");
        library.Register(this);
        return this;
    }

    public DomainHostBuilder RegisterAnnotation(IAnnotationSyntax syntax) {
        ArgumentNullException.ThrowIfNull(syntax);
        Annotations.Register(syntax);
        return this;
    }

    public DomainHostBuilder RegisterFold(
        string rule,
        string pattern,
        Func<Poly.Grammar.MatchResult<Parsing.DslToken, Parsing.DslTokenKind>, DomainExpression> fold) {
        ExpressionForms.RegisterFold(rule, pattern, fold);
        return this;
    }

    public DomainHostBuilder RegisterBinaryFold(IBinaryExpressionFold fold) {
        ArgumentNullException.ThrowIfNull(fold);
        ExpressionForms.RegisterBinaryFold(fold);
        return this;
    }

    public DomainHostBuilder AddStorageConvention(IStorageConvention convention) {
        ArgumentNullException.ThrowIfNull(convention);
        _storageConventions.Add(convention);
        return this;
    }

    public DomainHostBuilder AddArtifactContributor(IArtifactContributor contributor) {
        ArgumentNullException.ThrowIfNull(contributor);
        _artifacts.Add(contributor);
        return this;
    }

    public DomainParserInputs BuildParserInputs() =>
        new(Annotations, ExpressionForms);

    public DomainAnalysisInputs BuildAnalysisInputs() =>
        new(TypeMaps, _storageConventions);

    public DomainHost Build() =>
        new(BuildParserInputs(), BuildAnalysisInputs(), Meaning) {
            Artifacts = [.. _artifacts]
        };
}