using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// The one IRI resolution shared by the RDF projections (JSON-LD and SHACL). A declared
/// term IRI wins; an untagged element gets a stable <c>urn:poly:</c> identifier. Two
/// exporters must not invent different identities for the same element.
/// </summary>
internal static class DomainIris {
    /// <summary>One IRI per named domain type (entity, value type, enum), ordinal-keyed.</summary>
    public static Dictionary<string, string> Types(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Types
            .OfType<DomainType>()
            .Where(t => t is Entity or ValueType or EnumType)
            .ToDictionary(
                t => t.Name,
                t => TermFacets.Read(t.Facets)?.Iri ?? $"urn:poly:{domain.Name}/{t.Name}",
                StringComparer.Ordinal);
    }

    public static string Property(Domain domain, string typeName, Property property) =>
        TermFacets.Read(property.Facets)?.Iri
        ?? $"urn:poly:{domain.Name}/{typeName}/{property.Name}";

    public static string Navigation(Domain domain, string entityName, Relationship navigation) =>
        $"urn:poly:{domain.Name}/{entityName}/{navigation.Name}";

    /// <summary>Joins a local name to a class IRI; a base ending in <c>#</c> or <c>/</c> is used as-is.</summary>
    public static string Join(string classIri, string localName) =>
        classIri.EndsWith('#') || classIri.EndsWith('/')
            ? classIri + localName
            : classIri + "/" + localName;

    public static string Range(
        DomainTypeReference reference,
        IReadOnlyDictionary<string, DomainType> byName,
        IReadOnlyDictionary<string, string> typeIris) {
        ArgumentNullException.ThrowIfNull(reference);
        if (!byName.TryGetValue(reference.TypeName, out var type))
            throw new InvalidOperationException(
                $"Type '{reference.TypeName}' is not defined in the domain.");

        return type switch {
            PrimitiveType primitive => Xsd(primitive.Semantics),
            _ => typeIris[type.Name],
        };
    }

    public static string Xsd(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => "http://www.w3.org/2001/XMLSchema#boolean",
        PrimitiveSemantics.Number => "http://www.w3.org/2001/XMLSchema#integer",
        PrimitiveSemantics.Decimal => "http://www.w3.org/2001/XMLSchema#decimal",
        PrimitiveSemantics.Text => "http://www.w3.org/2001/XMLSchema#string",
        PrimitiveSemantics.Uuid => "http://www.w3.org/2001/XMLSchema#string",
        PrimitiveSemantics.Binary => "http://www.w3.org/2001/XMLSchema#base64Binary",
        PrimitiveSemantics.Date => "http://www.w3.org/2001/XMLSchema#date",
        PrimitiveSemantics.Time => "http://www.w3.org/2001/XMLSchema#time",
        PrimitiveSemantics.DateTime => "http://www.w3.org/2001/XMLSchema#dateTime",
        PrimitiveSemantics.Duration => "http://www.w3.org/2001/XMLSchema#duration",
        _ => throw new InvalidOperationException($"Unsupported primitive semantics '{semantics}'."),
    };

    /// <summary>The node-shape IRI for an entity; SHACL shapes are not ontology classes.</summary>
    public static string Shape(Domain domain, string typeName) =>
        $"urn:poly:{domain.Name}/shapes/{typeName}";
}