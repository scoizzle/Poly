namespace Poly.DomainModeling.Compile;

/// <summary>
/// The inputs a session's library selection came from: the catalog the ids
/// resolve against, the implicit id set a document without <c>uses</c> falls
/// back to, the host target, and the host's storage vendor and host-loaded
/// libraries. A reload reuses them, so it never falls back to
/// <see cref="ExtensionCatalog.Core"/>.
/// </summary>
public sealed record SessionOptions(
    ExtensionCatalog Catalog,
    IReadOnlyList<string> ImplicitIds,
    string? Target = null) {
    /// <summary>
    /// The catalog the ids resolve against. A null parameter fails closed here;
    /// a value set through <c>with</c> is re-checked by
    /// <see cref="LibrarySelection.Resolve"/>.
    /// </summary>
    public ExtensionCatalog Catalog { get; init; } =
        Catalog ?? throw new ArgumentNullException(nameof(Catalog));

    /// <summary>
    /// The ids a document that lists no <c>uses</c> falls back to. Blank or
    /// duplicate entries are rejected here; a value set through <c>with</c> is
    /// re-checked by <see cref="LibrarySelection.Resolve"/>.
    /// </summary>
    public IReadOnlyList<string> ImplicitIds { get; init; } =
        ValidateIds(ImplicitIds, nameof(ImplicitIds));

    /// <summary>
    /// The facet pack (<c>column</c>/<c>table</c>) as a session fact rather than a
    /// factory: the compile pipeline's pack, set by <c>PolyCompiler</c>. It never becomes an
    /// extension id, so a reload keeps it.
    /// </summary>
    internal bool Authoring { get; init; }

    /// <summary>
    /// The storage library the host chose (a vendor id, or the generic persistence id), or null
    /// when the host chose none. <see cref="LibrarySelection.Resolve"/> replaces a listed
    /// <c>persistence</c> with it in place and appends it only when no storage compiler is
    /// listed. Only a host that picks a database (a facade, not a document) sets it.
    /// </summary>
    public string? StorageVendor { get; init; }

    /// <summary>
    /// Libraries the host loaded outside the document's <c>uses</c>. They join the selection
    /// once, after it. Blank or repeated entries are rejected here; a value set through
    /// <c>with</c> is re-checked by <see cref="LibrarySelection.Resolve"/>.
    /// </summary>
    public IReadOnlyList<string> HostIds {
        get => _hostIds;
        init => _hostIds = ValidateIds(value, nameof(HostIds));
    }

    private readonly IReadOnlyList<string> _hostIds = [];

    /// <summary>Options with no implicit ids beyond the catalog's.</summary>
    public static SessionOptions Bare(ExtensionCatalog catalog) => new(catalog, []);

    /// <summary>
    /// Rejects a null set, a blank id and a repeated id. Shared with
    /// <see cref="LibrarySelection.Resolve"/> because <c>with</c> bypasses the
    /// property initializers above. One rule per input kind, whatever the entry
    /// point: a blank id is an <see cref="ArgumentException"/>, a repeated one an
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static IReadOnlyList<string> ValidateIds(IReadOnlyList<string>? implicitIds, string property) {
        ArgumentNullException.ThrowIfNull(implicitIds);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in implicitIds) {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Domain extension id must be non-empty.", property);
            if (!seen.Add(id))
                throw new InvalidOperationException(
                    $"{property} lists extension '{id}' more than once.");
        }
        return implicitIds;
    }
}