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