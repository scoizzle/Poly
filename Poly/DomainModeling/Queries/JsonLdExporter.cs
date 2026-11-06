using System.Text.Json;
using System.Text.Json.Nodes;

using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Projects a <see cref="Domain"/> as JSON-LD: an <c>@context</c> plus an RDFS
/// <c>@graph</c>. Entities, value types, and enums become <c>rdfs:Class</c> nodes;
/// properties and navigations become <c>rdf:Property</c> nodes with
/// <c>rdfs:domain</c>/<c>rdfs:range</c>; enum members become instances of their enum
/// class. Term IRIs are used as written; an untagged element gets a stable
/// <c>urn:poly:</c> identifier so the graph stays complete.
/// </summary>
public static class JsonLdExporter {
    public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    public const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    public const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
    };

    /// <summary>Returns the JSON-LD text for <paramref name="domain"/>, deterministic for equal domains.</summary>
    public static string Export(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var byName = domain.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var typeIris = DomainIris.Types(domain);
        var types = domain.Types
            .OfType<DomainType>()
            .Where(t => t is Entity or ValueType or EnumType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        var context = new JsonObject {
            ["rdf"] = Rdf,
            ["rdfs"] = Rdfs,
            ["xsd"] = Xsd,
        };
        var graph = new JsonArray();
        var typeNames = new List<(string Name, string Iri)>();
        var memberNames = new List<(string Name, string Owner, string Iri)>();

        foreach (var type in types) {
            var typeIri = typeIris[type.Name];
            typeNames.Add((type.Name, typeIri));
            graph.Add(ClassNode(typeIri, type));

            switch (type) {
                case Entity entity:
                    var propertyNames = entity.Properties
                        .Select(p => p.Name)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var property in entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
                        var propertyIri = DomainIris.Property(domain, entity.Name, property);
                        memberNames.Add((property.Name, entity.Name, propertyIri));
                        graph.Add(PropertyNode(
                            propertyIri, property, typeIri, DomainIris.Range(property.Type, byName, typeIris)));
                    }
                    foreach (var navigation in entity.Navigations.OrderBy(n => n.Name, StringComparer.Ordinal)) {
                        if (propertyNames.Contains(navigation.Name))
                            throw new InvalidOperationException(
                                $"Entity '{entity.Name}' declares both a property and a navigation named '{navigation.Name}'.");
                        var navigationIri = DomainIris.Navigation(domain, entity.Name, navigation);
                        memberNames.Add((navigation.Name, entity.Name, navigationIri));
                        graph.Add(PropertyNode(
                            navigationIri,
                            label: navigation.Name,
                            description: null,
                            domainIri: typeIri,
                            rangeIri: DomainIris.Range(navigation.Target, byName, typeIris)));
                    }
                    break;

                case ValueType valueType:
                    foreach (var property in valueType.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
                        var propertyIri = DomainIris.Property(domain, valueType.Name, property);
                        memberNames.Add((property.Name, valueType.Name, propertyIri));
                        graph.Add(PropertyNode(
                            propertyIri, property, typeIri, DomainIris.Range(property.Type, byName, typeIris)));
                    }
                    break;

                case EnumType enumType:
                    foreach (var member in enumType.MemberNames) {
                        var facets = enumType.MemberFacets.GetValueOrDefault(member);
                        var term = TermFacets.Read(facets);
                        var memberIri = term?.Iri ?? DomainIris.Join(typeIri, term?.Term ?? member);
                        memberNames.Add((member, enumType.Name, memberIri));

                        var node = new JsonObject {
                            ["@id"] = memberIri,
                            ["@type"] = typeIri,
                            ["rdfs:label"] = term?.Label ?? member,
                        };
                        AddComment(node, term?.Description);
                        graph.Add(node);
                    }
                    break;
            }
        }

        JsonLdContext.AddTerms(context, typeNames, memberNames);

        return new JsonObject {
            ["@context"] = context,
            ["@graph"] = graph,
        }.ToJsonString(WriteOptions);
    }

    private static JsonObject ClassNode(string iri, DomainType type) {
        var term = TermFacets.Read(type.Facets);
        var node = new JsonObject {
            ["@id"] = iri,
            ["@type"] = "rdfs:Class",
            ["rdfs:label"] = term?.Label ?? type.Name,
        };
        AddComment(node, term?.Description);
        return node;
    }

    private static JsonObject PropertyNode(
        string iri,
        Property property,
        string domainIri,
        string rangeIri) {
        var term = TermFacets.Read(property.Facets);
        return PropertyNode(iri, term?.Label ?? property.Name, term?.Description, domainIri, rangeIri);
    }

    private static JsonObject PropertyNode(
        string iri,
        string label,
        string? description,
        string domainIri,
        string rangeIri) {
        var node = new JsonObject {
            ["@id"] = iri,
            ["@type"] = "rdf:Property",
            ["rdfs:label"] = label,
            ["rdfs:domain"] = new JsonObject { ["@id"] = domainIri },
            ["rdfs:range"] = new JsonObject { ["@id"] = rangeIri },
        };
        AddComment(node, description);
        return node;
    }

    private static void AddComment(JsonObject node, string? description) {
        if (!string.IsNullOrWhiteSpace(description))
            node["rdfs:comment"] = description;
    }
}