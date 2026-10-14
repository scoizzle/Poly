namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Optional storage-facet spellings (<c>column</c> / <c>table</c>). Not language.
/// Compiler and MCP authoring load this; language-only resolve does not.
/// </summary>
public sealed class StorageFacetLibrary : IDomainLibrary {
    public string Id => "storage";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Annotations.Register(new ColumnAnnotationSyntax());
        builder.Annotations.Register(new TableAnnotationSyntax());
    }

    /// <summary>
    /// Registers <c>column</c>/<c>table</c> when no pack has. Does not add the
    /// <c>storage</c> extension id, so a source <c>uses</c> list that omits it
    /// still keeps the annotations across <c>WithDomain</c>.
    /// </summary>
    public static void EnsureRegistered(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Annotations.CanAccept("column") || builder.Annotations.CanAccept("table"))
            return;
        new StorageFacetLibrary().Register(builder);
    }
}