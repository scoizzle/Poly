using System.Collections.Concurrent;

using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Libraries.Tests;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Resolves extension ids to libraries. The domain stores ids; the catalog is
/// the door. Unknown id fails closed. Core knows in-assembly libraries only;
/// the compiler adds vendor libraries.
/// </summary>
public sealed class ExtensionCatalog {
    public const string TemporalId = "temporal";
    public const string StorageFacetsId = "storage";

    /// <summary>Authored language ids. Temporal is implicit at Load, not a product seed.</summary>
    public static readonly IReadOnlyList<string> ProductLanguage = [];

    /// <summary>MCP authoring seed: <c>column</c>/<c>table</c>. Temporal stays implicit.</summary>
    public static readonly IReadOnlyList<string> ProductAuthoring = [StorageFacetsId];

    /// <summary>Names a seed can be looked up by: <c>authoring</c> and <c>language</c>.</summary>
    public static IReadOnlyList<string> SeedNames { get; } = ["authoring", "language"];

    /// <summary>
    /// Looks up a named seed: <c>authoring</c> is <see cref="ProductAuthoring"/>, <c>language</c>
    /// is <see cref="ProductLanguage"/>. Unknown name fails closed.
    /// </summary>
    public static IReadOnlyList<string> Seed(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToLowerInvariant() switch {
            "authoring" => ProductAuthoring,
            "language" => ProductLanguage,
            _ => throw new ArgumentException(
                $"Unknown seed '{name}'. Valid values: {string.Join(", ", SeedNames)}.", nameof(name)),
        };
    }

    /// <summary>Session for <see cref="ProductLanguage"/>.</summary>
    public DomainSession Language => DomainSession.ForExtensions(ProductLanguage, this);

    /// <summary>Session for <see cref="ProductAuthoring"/>.</summary>
    public DomainSession Authoring => DomainSession.ForExtensions(ProductAuthoring, this);

    private readonly Dictionary<string, IDomainLibrary> _libraries;
    private readonly ConcurrentDictionary<string, Lazy<LibraryProbe>> _probes = new(StringComparer.Ordinal);

    public static ExtensionCatalog Core { get; } = new ExtensionCatalog()
        .With(new TemporalLibrary())
        .With(new StorageFacetLibrary())
        .With(new PersistenceEmitLibrary())
        .With(new TestsLibrary());

    public ExtensionCatalog() {
        _libraries = new Dictionary<string, IDomainLibrary>(StringComparer.Ordinal);
    }

    private ExtensionCatalog(Dictionary<string, IDomainLibrary> libraries) {
        _libraries = libraries;
    }

    public ExtensionCatalog With(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(library.Id))
            throw new ArgumentException("Library id must be non-empty.", nameof(library));
        if (_libraries.ContainsKey(library.Id))
            throw new InvalidOperationException($"Extension '{library.Id}' is already in the catalog.");
        var next = new Dictionary<string, IDomainLibrary>(_libraries, StringComparer.Ordinal) {
            [library.Id] = library
        };
        return new ExtensionCatalog(next);
    }

    public IDomainLibrary Resolve(string id) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_libraries.TryGetValue(id, out var library))
            throw new InvalidOperationException(
                $"Unknown domain extension '{id}'. Record only ids this catalog can resolve.");
        return library;
    }

    /// <summary>True when this catalog can resolve <paramref name="id"/>.</summary>
    public bool Contains(string id) =>
        id is not null && _libraries.ContainsKey(id);

    /// <summary>
    /// Every extension id this catalog resolves, ordinal-sorted. Reads the
    /// dictionary keys and never probes a library.
    /// </summary>
    public IReadOnlyList<string> KnownIds =>
        _libraries.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Every host target the catalog's libraries declare, ordinal-sorted and
    /// distinct. Probes each library once, as <see cref="Info"/> does.
    /// </summary>
    public IReadOnlyList<string> KnownTargets => SortedDistinct(
        _libraries.Keys.SelectMany(id => Probe(id).Info.HostTargets));

    /// <summary>
    /// The library's read-only facts, from one cached
    /// <see cref="IDomainLibrary.Register"/> on a throwaway builder. An unknown id
    /// fails through <see cref="Resolve"/>.
    /// </summary>
    /// <remarks>
    /// The probe loads only this library (no core) and then freezes the builder, so
    /// <see cref="IDomainLibrary.Register"/> must be repeatable on a fresh builder.
    /// A probe exception propagates.
    /// </remarks>
    public LibraryInfo Info(string id) {
        _ = Resolve(id);
        return Probe(id).Info;
    }

    /// <summary>
    /// The catalog ids whose library installs a compiler whose
    /// <see cref="IConstraintCompiler.Applies"/> is true for
    /// <paramref name="constraint"/>, ordinal-sorted. Reports the catalog key, never
    /// the compiler's own <see cref="IConstraintCompiler.LibraryId"/>. A throwing
    /// <c>Applies</c> propagates, and each call returns a fresh list.
    /// </summary>
    public IReadOnlyList<string> ProvidersOf(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        var providers = new List<string>();
        foreach (var id in KnownIds) {
            if (Probe(id).Compilers.Any(compiler => compiler.Applies(constraint)))
                providers.Add(id);
        }
        return providers.ToArray();
    }

    private LibraryProbe Probe(string id) =>
        _probes.GetOrAdd(
            id,
            libraryId => new Lazy<LibraryProbe>(
                () => CreateProbe(libraryId),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private LibraryProbe CreateProbe(string id) {
        var library = Resolve(id);
        var builder = SessionBuilder.CreateEmpty();
        builder.Load(library);
        var session = builder.Build(SessionOptions.Bare(this));
        return new LibraryProbe(
            new LibraryInfo(
                id,
                library.PrimitiveSeeds,
                SortedDistinct(session.ContractContributions.SelectMany(contribution => contribution.Targets)),
                SortedDistinct(session.ContractContributions.Select(contribution => contribution.Surface.Name))),
            session.ConstraintCompilers);
    }

    private static IReadOnlyList<string> SortedDistinct(IEnumerable<string> values) => values
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}