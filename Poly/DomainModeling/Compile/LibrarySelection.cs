using Poly.DomainModeling.Libraries.Storage;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// The one place a document's <c>uses</c> ids are chosen against a session's
/// implicit id set. Today's rule is replace: a non-empty <c>uses</c> list wins
/// outright. Additive or vendor-aware selection (vendor storage replacing
/// <c>persistence</c>) is C-03/C-04 behind this same signature.
/// </summary>
public static class LibrarySelection {
    /// <summary>
    /// The ids a document loads: its non-empty <c>uses</c> list, else the options'
    /// implicit set. A blank id is an <see cref="ArgumentException"/>; a repeated
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
        SessionOptions.ValidateImplicitIds(options.ImplicitIds);
        if (documentUses.Count > 0) {
            ValidateDocumentUses(documentUses);
            foreach (var id in documentUses)
                catalog.Resolve(id);
            return documentUses;
        }
        foreach (var id in options.ImplicitIds)
            catalog.Resolve(id);
        return options.ImplicitIds;
    }

    /// <summary>
    /// Loads exactly <paramref name="ids"/> against the options' catalog, with no
    /// <see cref="Resolve"/> substitution. A blank or repeated id and an unknown id
    /// throw <see cref="InvalidOperationException"/>. The session keeps
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
                throw new InvalidOperationException("Domain extension id must be non-empty.");
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