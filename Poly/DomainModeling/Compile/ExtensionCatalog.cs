using Poly.DomainModeling.Libraries.Temporal;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Resolves extension ids to libraries. The domain stores ids; the catalog is
/// the door. Unknown id fails closed. Core knows in-assembly libraries only;
/// the compiler adds vendor libraries.
/// </summary>
public sealed class ExtensionCatalog {
    public const string TemporalId = "temporal";
    public const string StorageFacetsId = "storage";

    /// <summary>SDK language default written onto new product units.</summary>
    public static readonly IReadOnlyList<string> ProductLanguage = [TemporalId];

    /// <summary>MCP authoring seed: language plus <c>column</c>/<c>table</c>.</summary>
    public static readonly IReadOnlyList<string> ProductAuthoring = [TemporalId, StorageFacetsId];

    /// <summary>Session for <see cref="ProductLanguage"/>.</summary>
    public DomainSession Language => DomainSession.ForExtensions(ProductLanguage, this);

    /// <summary>Session for <see cref="ProductAuthoring"/>.</summary>
    public DomainSession Authoring => DomainSession.ForExtensions(ProductAuthoring, this);

    private readonly Dictionary<string, IDomainLibrary> _libraries;

    public static ExtensionCatalog Core { get; } = new ExtensionCatalog()
        .With(new TemporalLibrary())
        .With(new StorageFacetLibrary());

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
}