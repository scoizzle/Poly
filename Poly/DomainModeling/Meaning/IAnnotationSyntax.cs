using Poly.DomainModeling.Dispatch;
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

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// A pack-provided handler for one annotation keyword (e.g. <c>"column"</c>, <c>"table"</c>, <c>"json"</c>).
/// The parser natively parses <c>keyword("arg1", "arg2")</c> into <see cref="Annotation"/> records;
/// packs implement this interface primarily for <b>printing</b> (and optionally for custom
/// facet types that <see cref="Annotation"/> cannot represent).
/// </summary>
public interface IAnnotationSyntax {
    /// <summary>The keyword that triggers this handler, e.g. <c>"column"</c>.</summary>
    string Keyword { get; }

    /// <summary>
    /// If this handler can print the given facet, returns the .poly text.
    /// Returns <c>false</c> if the facet is not recognized.
    /// </summary>
    bool TryPrint(Facet facet, out string text);
}