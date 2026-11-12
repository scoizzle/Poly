using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// The one place a document's <c>uses</c> ids are chosen against a session's
/// implicit id set: a non-empty <c>uses</c> list replaces the set, an empty one
/// loads it, so a <c>unique</c> property needs <c>persistence</c> or a vendor
/// library (a <c>sqlite</c>/<c>sqlserver</c> package) in that list.
/// </summary>
public static class LibrarySelection {
    /// <summary>
    /// The ids a document loads: its non-empty <c>uses</c> list, else the options'
    /// implicit set, then the host-loaded ids not already listed, then the host's storage vendor
    /// (<see cref="SessionOptions.StorageVendor"/>) in place of <c>persistence</c>. A blank id is an <see cref="ArgumentException"/>; a repeated
    /// <c>uses</c> id and an id the catalog cannot resolve throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> documentUses,
        SessionOptions options) {
        ArgumentNullException.ThrowIfNull(documentUses);
        ArgumentNullException.ThrowIfNull(options);
        var catalog = options.Catalog;
        ArgumentNullException.ThrowIfNull(catalog);
        // `with` bypasses SessionOptions' property validation, so re-check here.
        SessionOptions.ValidateIds(options.ImplicitIds, nameof(SessionOptions.ImplicitIds));
        SessionOptions.ValidateIds(options.HostIds, nameof(SessionOptions.HostIds));
        var fromDocument = documentUses.Count > 0;
        if (fromDocument)
            ValidateDocumentUses(documentUses);
        var chosen = fromDocument ? documentUses : options.ImplicitIds;
        foreach (var id in chosen)
            catalog.Resolve(id);
        foreach (var id in options.HostIds)
            catalog.Resolve(id);
        if (options.HostIds.Count == 0 && options.StorageVendor is null)
            return chosen;

        var ids = new List<string>(chosen);
        foreach (var id in options.HostIds) {
            if (!ids.Contains(id, StringComparer.Ordinal))
                ids.Add(id);
        }
        if (options.StorageVendor is { } vendor)
            ApplyStorageVendor(ids, vendor, fromDocument, catalog);
        return ids;
    }

    /// <summary>
    /// A storage compiler other than generic <c>persistence</c> in the list wins: it is the
    /// vendor, so no second one is added, and a <c>persistence</c> beside it goes (an author's own
    /// only when the host's vendor is a different library). With none, a listed
    /// <c>persistence</c> becomes the vendor where it stands, and with no storage compiler at all
    /// the vendor is appended.
    /// </summary>
    private static void ApplyStorageVendor(
        List<string> ids, string vendor, bool fromDocument, ExtensionCatalog catalog) {
        catalog.Resolve(vendor);
        var providers = catalog.ProvidersOf(new UniqueConstraint());
        if (!providers.Contains(vendor, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Storage vendor '{vendor}' is not a storage library: it installs no compiler for a unique constraint.");
        var persistence = ids.IndexOf(PersistenceEmitLibrary.LibraryId);
        var other = ids.Exists(id =>
            !string.Equals(id, PersistenceEmitLibrary.LibraryId, StringComparison.Ordinal)
            && providers.Contains(id, StringComparer.Ordinal));
        if (other) {
            var vendorIsPersistence = string.Equals(vendor, PersistenceEmitLibrary.LibraryId, StringComparison.Ordinal);
            if (persistence >= 0 && (!fromDocument || !vendorIsPersistence))
                ids.RemoveAt(persistence);
        }
        else if (persistence >= 0)
            ids[persistence] = vendor;
        else
            ids.Add(vendor);
    }

    /// <summary>
    /// Loads exactly <paramref name="ids"/> against the options' catalog, with no
    /// <see cref="Resolve"/> substitution. A blank id is an
    /// <see cref="ArgumentException"/>; a repeated or unknown id throws
    /// <see cref="InvalidOperationException"/>. The session keeps
    /// <paramref name="options"/> so a reload resolves against the same catalog.
    /// </summary>
    public static DomainSession Load(IReadOnlyList<string> ids, SessionOptions options) {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(options);
        var catalog = options.Catalog;
        ArgumentNullException.ThrowIfNull(catalog);
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        if (!ids.Any(id => string.Equals(id, ExtensionCatalog.TemporalId, StringComparison.Ordinal)))
            builder.LoadTemporal();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Domain extension id must be non-empty.", nameof(ids));
            if (!seen.Add(id))
                throw new InvalidOperationException($"Domain lists extension '{id}' more than once.");
            builder.Load(catalog.Resolve(id));
        }
        if (options.Authoring)
            StorageFacetLibrary.EnsureRegistered(builder);
        return builder.Build(options);
    }

    /// <summary>Loads the libraries <see cref="Resolve"/> selects for the document's <c>uses</c>.</summary>
    public static DomainSession Load(DslSyntaxTree tree, SessionOptions options) {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(options);
        return Load(Resolve(tree.Uses, options), options);
    }

    private static void ValidateDocumentUses(IReadOnlyList<string> documentUses) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in documentUses) {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Domain extension id must be non-empty.", nameof(documentUses));
            if (!seen.Add(id))
                throw new InvalidOperationException($"Domain lists extension '{id}' more than once.");
        }
    }
}