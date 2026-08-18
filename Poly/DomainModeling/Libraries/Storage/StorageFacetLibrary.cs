using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

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