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
}