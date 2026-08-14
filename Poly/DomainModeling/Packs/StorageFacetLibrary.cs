using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling.Packs;

/// <summary>
/// Optional storage-facet spellings (<c>column</c> / <c>table</c>). Not language.
/// Compiler and MCP authoring load this; language-only resolve does not.
/// </summary>
public sealed class StorageFacetLibrary : IDomainLibrary {
    public string Id => "storage";

    public void Register(HostSurfaces surfaces) {
        ArgumentNullException.ThrowIfNull(surfaces);
        surfaces.Annotations.Register(new ColumnAnnotationSyntax());
        surfaces.Annotations.Register(new TableAnnotationSyntax());
    }
}