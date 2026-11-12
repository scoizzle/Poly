namespace Poly.DomainModeling.Compile;

/// <summary>
/// The inputs a session's library selection came from: the catalog the ids
/// resolve against, the implicit id set a document without <c>uses</c> falls
/// back to, and the host target. A reload reuses them, so it never falls back
/// to <see cref="ExtensionCatalog.Core"/>.
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
        ValidateImplicitIds(ImplicitIds);

    /// <summary>
    /// The facet pack (<c>column</c>/<c>table</c>) as a session fact rather than a
    /// factory. Internal and temporary: C-04 deletes it once storage is implicit.
    /// </summary>
    internal bool Authoring { get; init; }

    /// <summary>S1 stub.</summary>
    public string? StorageVendor { get; init; }

    /// <summary>S1 stub.</summary>
    public IReadOnlyList<string> HostIds { get; init; } = [];

    /// <summary>Options with no implicit ids beyond the catalog's.</summary>
    public static SessionOptions Bare(ExtensionCatalog catalog) => new(catalog, []);

    /// <summary>
    /// Rejects a null set, a blank id and a repeated id. Shared with
    /// <see cref="LibrarySelection.Resolve"/> because <c>with</c> bypasses the
    /// property initializers above. One rule per input kind, whatever the entry
    /// point: a blank id is an <see cref="ArgumentException"/>, a repeated one an
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static IReadOnlyList<string> ValidateImplicitIds(IReadOnlyList<string>? implicitIds) {
        ArgumentNullException.ThrowIfNull(implicitIds);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in implicitIds) {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Domain extension id must be non-empty.", nameof(implicitIds));
            if (!seen.Add(id))
                throw new InvalidOperationException(
                    $"Implicit ids list extension '{id}' more than once.");
        }
        return implicitIds;
    }
}