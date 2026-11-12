using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Entity = Poly.DomainModeling.Ontology.Entity;
using EnumType = Poly.DomainModeling.Ontology.EnumType;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// Projects a <see cref="Domain"/> as SHACL (JSON-LD syntax) over the same IRIs the RDFS
/// export uses. Each entity gets a <c>sh:NodeShape</c> targeting its class; each property
/// and navigation gets a <c>sh:PropertyShape</c> carrying the constraints Poly already
/// proved. A fact SHACL cannot express is kept as <c>x-poly-*</c>, never dropped silently.
/// Shapes and their property shapes follow declaration order (Q12).
/// </summary>
public static class ShaclExporter {
    public const string Sh = "http://www.w3.org/ns/shacl#";

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
    };

    /// <summary>Returns the SHACL text for <paramref name="domain"/>, deterministic for equal domains.</summary>
    public static string Export(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var types = DomainExportOrder.Types(domain);
        var byName = domain.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var typeIris = DomainIris.Types(domain);

        var context = new JsonObject {
            ["sh"] = Sh,
            ["rdf"] = JsonLdExporter.Rdf,
            ["rdfs"] = JsonLdExporter.Rdfs,
            ["xsd"] = JsonLdExporter.Xsd,
            // The extension keys must be defined, or a JSON-LD processor drops them from
            // the RDF graph as unknown terms.
            ["x-poly-unique"] = "urn:poly:vocab/unique",
            ["x-poly-default"] = "urn:poly:vocab/default",
            ["x-poly-minInclusive"] = "urn:poly:vocab/minInclusive",
            ["x-poly-maxInclusive"] = "urn:poly:vocab/maxInclusive",
            ["x-poly-constraints"] = "urn:poly:vocab/constraints",
        };
        var graph = new JsonArray();
        var typeNames = new List<(string Name, string Iri)>();
        var memberNames = new List<(string Name, string Owner, string Iri)>();

        foreach (var type in types) {
            typeNames.Add((type.Name, typeIris[type.Name]));
            switch (type) {
                case Entity entity:
                    graph.Add(EntityShape(
                        domain, entity, typeIris[entity.Name], byName, typeIris, memberNames));
                    break;
                case ValueType valueType:
                    graph.Add(ValueTypeShape(
                        domain, valueType, typeIris[valueType.Name], byName, typeIris, memberNames));
                    break;
                case EnumType enumType:
                    foreach (var member in enumType.MemberNames) {
                        var term = TermFacets.Read(enumType.MemberFacets.GetValueOrDefault(member));
                        memberNames.Add((member, enumType.Name,
                            term?.Iri ?? DomainIris.Join(typeIris[enumType.Name], term?.Term ?? member)));
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

    private static JsonObject EntityShape(
        Domain domain,
        Entity entity,
        string typeIri,
        IReadOnlyDictionary<string, DomainType> byName,
        IReadOnlyDictionary<string, string> typeIris,
        List<(string Name, string Owner, string Iri)> memberNames) {
        var term = TermFacets.Read(entity.Facets);
        var shape = new JsonObject {
            ["@id"] = DomainIris.Shape(domain, entity.Name),
            ["@type"] = "sh:NodeShape",
            ["sh:targetClass"] = new JsonObject { ["@id"] = typeIri },
            ["sh:closed"] = true,
            ["sh:ignoredProperties"] = RdfList(new JsonArray(new JsonObject { ["@id"] = "rdf:type" })),
        };
        if (term?.Label is { } label)
            shape["rdfs:label"] = label;
        if (term?.Description is { } description)
            shape["rdfs:comment"] = description;

        var entryAssigned = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);
        var properties = new JsonArray();
        var propertyNames = entity.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var member in EntityDeclarationOrder.Members(entity)) {
            switch (member.Node) {
                case Property property:
                    var propertyIri = DomainIris.Property(domain, entity.Name, property);
                    memberNames.Add((property.Name, entity.Name, propertyIri));
                    properties.Add(PropertyShape(propertyIri, property, entryAssigned, byName, typeIris));
                    break;
                case Relationship navigation:
                    if (propertyNames.Contains(navigation.Name))
                        throw new InvalidOperationException(
                            $"Entity '{entity.Name}' declares both a property and a navigation named '{navigation.Name}'.");

                    var navigationIri = DomainIris.Navigation(domain, entity.Name, navigation);
                    memberNames.Add((navigation.Name, entity.Name, navigationIri));
                    var propertyShape = new JsonObject {
                        ["@type"] = "sh:PropertyShape",
                        ["sh:path"] = new JsonObject { ["@id"] = navigationIri },
                        ["sh:minCount"] = 0,
                        ["sh:class"] = new JsonObject {
                            ["@id"] = DomainIris.Range(navigation.Target, byName, typeIris),
                        },
                    };
                    if (navigation.Cardinality is RelationshipCardinality.OneToOne
                        or RelationshipCardinality.ManyToOne) {
                        propertyShape["sh:maxCount"] = 1;
                    }
                    properties.Add(propertyShape);
                    break;
            }
        }

        if (properties.Count > 0)
            shape["sh:property"] = properties;
        return shape;
    }

    private static JsonObject ValueTypeShape(
        Domain domain,
        ValueType valueType,
        string typeIri,
        IReadOnlyDictionary<string, DomainType> byName,
        IReadOnlyDictionary<string, string> typeIris,
        List<(string Name, string Owner, string Iri)> memberNames) {
        var term = TermFacets.Read(valueType.Facets);
        var shape = new JsonObject {
            ["@id"] = DomainIris.Shape(domain, valueType.Name),
            ["@type"] = "sh:NodeShape",
            ["sh:targetClass"] = new JsonObject { ["@id"] = typeIri },
            ["sh:closed"] = true,
            ["sh:ignoredProperties"] = RdfList(new JsonArray(new JsonObject { ["@id"] = "rdf:type" })),
        };
        if (term?.Label is { } label)
            shape["rdfs:label"] = label;
        if (term?.Description is { } description)
            shape["rdfs:comment"] = description;

        var entryAssigned = new HashSet<string>(StringComparer.Ordinal);
        var properties = new JsonArray();
        foreach (var property in valueType.Properties) {
            var propertyIri = DomainIris.Property(domain, valueType.Name, property);
            memberNames.Add((property.Name, valueType.Name, propertyIri));
            properties.Add(PropertyShape(propertyIri, property, entryAssigned, byName, typeIris));
        }
        if (properties.Count > 0)
            shape["sh:property"] = properties;
        return shape;
    }

    private static JsonObject PropertyShape(
        string propertyIri,
        Property property,
        IReadOnlySet<string> entryAssigned,
        IReadOnlyDictionary<string, DomainType> byName,
        IReadOnlyDictionary<string, string> typeIris) {
        if (!byName.TryGetValue(property.Type.TypeName, out var type))
            throw new InvalidOperationException(
                $"Property type '{property.Type.TypeName}' is not defined in the domain.");

        var term = TermFacets.Read(property.Facets);
        var shape = new JsonObject {
            ["@type"] = "sh:PropertyShape",
            ["sh:path"] = new JsonObject { ["@id"] = propertyIri },
        };
        if (term?.Label is { } label)
            shape["sh:name"] = label;
        if (term?.Description is { } description)
            shape["sh:description"] = description;

        if (type is PrimitiveType primitive) {
            shape["sh:datatype"] = new JsonObject { ["@id"] = DomainIris.Xsd(primitive.Semantics) };
        }
        else if (type is EnumType enumType) {
            shape["sh:class"] = new JsonObject { ["@id"] = typeIris[enumType.Name] };
            var members = new JsonArray();
            foreach (var member in enumType.MemberNames) {
                var memberTerm = TermFacets.Read(enumType.MemberFacets.GetValueOrDefault(member));
                members.Add(new JsonObject {
                    ["@id"] = memberTerm?.Iri
                        ?? DomainIris.Join(typeIris[enumType.Name], memberTerm?.Term ?? member),
                });
            }
            if (members.Count > 0)
                shape["sh:in"] = RdfList(members);
        }
        else {
            shape["sh:class"] = new JsonObject { ["@id"] = typeIris[type.Name] };
        }

        var required = DomainPresentation.IsRequired(property, entryAssigned);
        shape["sh:minCount"] = required ? 1 : 0;
        shape["sh:maxCount"] = 1;

        var unknown = new JsonArray();
        foreach (var constraint in property.Constraints) {
            switch (constraint) {
                case RangeConstraint range:
                    AddBound(shape, "sh:minInclusive", range.Minimum);
                    AddBound(shape, "sh:maxInclusive", range.Maximum);
                    break;
                case LengthConstraint length:
                    shape["sh:minLength"] = length.MinLength;
                    shape["sh:maxLength"] = length.MaxLength;
                    break;
                case PatternConstraint pattern:
                    shape["sh:pattern"] = pattern.Pattern;
                    break;
                case EqualityConstraint equality:
                    // sh:in (not sh:hasValue) keeps an optional property optional: an
                    // absent value satisfies minCount 0, while a present one is pinned.
                    shape["sh:in"] = RdfList(AllowedValues(equality, type, typeIris));
                    break;
                case UniqueConstraint:
                    shape["x-poly-unique"] = true;
                    break;
                case DefaultValueConstraint defaultValue:
                    shape["x-poly-default"] = DefaultText(defaultValue.Expression);
                    break;
                case RequiredConstraint:
                case OptionalConstraint:
                    break;
                default:
                    unknown.Add(constraint.GetType().Name);
                    break;
            }
        }
        if (unknown.Count > 0)
            shape["x-poly-constraints"] = RdfList(unknown);

        return shape;
    }

    private static JsonArray AllowedValues(
        EqualityConstraint equality,
        DomainType type,
        IReadOnlyDictionary<string, string> typeIris) {
        var values = new JsonArray();
        if (type is EnumType enumType
            && equality.ExpectedValue is string member
            && enumType.MemberNames.Contains(member, StringComparer.Ordinal)) {
            var term = TermFacets.Read(enumType.MemberFacets.GetValueOrDefault(member));
            values.Add(new JsonObject {
                ["@id"] = term?.Iri
                    ?? DomainIris.Join(typeIris[enumType.Name], term?.Term ?? member),
            });
            return values;
        }

        values.Add(Literal(equality.ExpectedValue, type));
        return values;
    }

    /// <summary>A typed literal; RDF-term equality needs the same datatype the shape declares.</summary>
    private static JsonObject Literal(object? value, DomainType type) {
        var node = new JsonObject {
            ["@value"] = JsonSerializer.SerializeToNode(value),
        };
        if (type is PrimitiveType primitive)
            node["@type"] = DomainIris.Xsd(primitive.Semantics);
        return node;
    }

    /// <summary>SHACL <c>sh:in</c> and <c>sh:ignoredProperties</c> take an RDF list, not a set.</summary>
    private static JsonObject RdfList(JsonArray items) => new() { ["@list"] = items };

    private static void AddBound(JsonObject shape, string keyword, object? value) {
        if (value is null)
            return;

        var node = JsonSerializer.SerializeToNode(value);
        if (node is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.Number)
            shape[keyword] = node;
        else
            shape["x-poly-" + keyword["sh:".Length..]] = node;
    }

    private static string DefaultText(DomainExpression expression) => expression switch {
        Literal { Value: null } => "null",
        Literal { Value: string text } => $"\"{text}\"",
        Literal { Value: { } value } => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        PropertyAccess access => access.Name,
        _ => "…",
    };
}