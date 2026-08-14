using Poly.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Packs.Temporal;
using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling;

/// <summary>
/// Resolved parse/print and analysis tables for a set of extension ids.
/// Prefer <see cref="Domain.ResolveHost"/> or <see cref="ExtensionCatalog.ResolveHost"/>.
/// </summary>
public sealed record DomainHost(
    DomainParserInputs Parser,
    DomainAnalysisInputs Analysis
);

/// <summary>
/// Immutable parser/printer tables for one session.
/// </summary>
public sealed class DomainParserInputs {
    /// <summary>Empty tables — no language libraries.</summary>
    public static DomainParserInputs Empty { get; } = new(new AnnotationRegistry());

    public AnnotationRegistry Annotations { get; }

    /// <summary>Expression forms, folds, and print mappings.</summary>
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
    /// <summary>Empty maps, conventions, and extra passes.</summary>
    public static DomainAnalysisInputs Empty { get; } = new(
        new TypeMappingRegistry(),
        [],
        []);

    public TypeMappingRegistry TypeMaps { get; }
    public IReadOnlyList<IStorageConvention> StorageConventions { get; }
    public IReadOnlyList<INodeAnalyzer> AdditionalPasses { get; }

    public DomainAnalysisInputs(
        TypeMappingRegistry typeMaps,
        IReadOnlyList<IStorageConvention> storageConventions,
        IReadOnlyList<INodeAnalyzer> additionalPasses) {
        ArgumentNullException.ThrowIfNull(typeMaps);
        ArgumentNullException.ThrowIfNull(storageConventions);
        ArgumentNullException.ThrowIfNull(additionalPasses);

        var duplicatePass = additionalPasses
            .GroupBy(p => p.PassName, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicatePass is not null) {
            throw new InvalidOperationException(
                $"Duplicate analyzer pass '{duplicatePass.Key}' in explicit analysis inputs.");
        }

        TypeMaps = typeMaps.Clone();
        StorageConventions = storageConventions.ToArray();
        AdditionalPasses = additionalPasses.ToArray();
    }
}

/// <summary>
/// Composes a <see cref="DomainHost"/> by loading libraries into parse and analysis surfaces.
/// </summary>
public sealed class DomainHostBuilder {
    private readonly List<IStorageConvention> _storageConventions = [];
    private readonly List<INodeAnalyzer> _analysisPasses = [];
    private readonly HashSet<string> _loadedIds = new(StringComparer.Ordinal);

    public AnnotationRegistry Annotations { get; } = new();
    public ExpressionFormRegistry ExpressionForms { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();

    /// <summary>No extensions loaded — used by the catalog when resolving ids.</summary>
    public static DomainHostBuilder CreateEmpty() => new();

    /// <summary>Loads Temporal. Prefer resolving <see cref="Domain.Extensions"/> in product paths.</summary>
    public static DomainHostBuilder Create() =>
        CreateEmpty().Load(new TemporalLibrary());

    /// <summary>Loads <c>column</c>/<c>table</c> spelling.</summary>
    public DomainHostBuilder WithStorageFacets() => Load(new StorageFacetLibrary());

    /// <summary>
    /// Loads <paramref name="library"/>. Duplicate <see cref="IDomainLibrary.Id"/> fails closed.
    /// </summary>
    public DomainHostBuilder Load(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.Id))
            throw new ArgumentException("Library id must be non-empty.", nameof(library));
        if (!_loadedIds.Add(library.Id))
            throw new InvalidOperationException($"A library with id '{library.Id}' is already loaded.");
        library.Register(new HostSurfaces(this));
        return this;
    }

    public DomainHostBuilder RegisterAnnotation(IAnnotationSyntax syntax) {
        ArgumentNullException.ThrowIfNull(syntax);
        Annotations.Register(syntax);
        return this;
    }

    public DomainHostBuilder RegisterExpressionForm(IExpressionPrimaryForm form) {
        ArgumentNullException.ThrowIfNull(form);
        ExpressionForms.Register(form);
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

    public DomainHostBuilder AddAnalysisPass(INodeAnalyzer pass) {
        ArgumentNullException.ThrowIfNull(pass);
        _analysisPasses.Add(pass);
        return this;
    }

    public DomainParserInputs BuildParserInputs() =>
        new(Annotations, ExpressionForms);

    public DomainAnalysisInputs BuildAnalysisInputs() =>
        new(TypeMaps, _storageConventions, _analysisPasses);

    public DomainHost Build() =>
        new(BuildParserInputs(), BuildAnalysisInputs());
}
