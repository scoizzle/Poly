using System.Text.Encodings.Web;
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
/// Projects a <see cref="Domain"/> as JSON Schema (draft 2020-12). This is a read-only
/// view over the domain's own facts: it invents no constraint the domain did not declare,
/// and the only Poly-specific additions are <c>x-poly-*</c> keys for facts JSON Schema
/// cannot express (<c>unique</c>).
/// <para>
/// Identity and behavior stay out: stages, actions, policies, and subscriptions are not
/// data shape. Navigations are included as references so the graph survives the export.
/// Property requiredness follows the DSL rule — required unless <c>optional</c>, and a
/// <c>default</c> makes the property omittable.
/// </para>
/// </summary>
public static class JsonSchemaExporter {
    /// <summary>The JSON Schema dialect this exporter emits.</summary>
    public const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Returns the schema text for <paramref name="domain"/>, deterministic for equal domains.</summary>
    public static string Export(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var byName = domain.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var defs = new JsonObject();

        foreach (var type in domain.Types
            .OfType<DomainType>()
            .Where(t => t is Entity or ValueType or EnumType)
            .OrderBy(t => t.Name, StringComparer.Ordinal)) {
            defs[type.Name] = type switch {
                EnumType enumType => EnumDefinition(enumType),
                Entity entity => ObjectDefinition(
                    entity.Name,
                    entity.Facets,
                    entity.Properties,
                    entity.Navigations,
                    byName,
                    EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity)),
                ValueType valueType => ObjectDefinition(
                    valueType.Name, valueType.Facets, valueType.Properties, [], byName, entryAssigned: null),
                _ => throw new InvalidOperationException($"Unsupported domain type '{type.Name}'."),
            };
        }

        var schema = new JsonObject {
            ["$schema"] = Draft202012,
            ["$id"] = $"urn:poly:{domain.Name}",
            ["title"] = domain.Name,
            ["description"] = $"JSON Schema generated from the Poly domain '{domain.Name}'.",
            ["$defs"] = defs,
        };

        return schema.ToJsonString(WriteOptions);
    }

    private static JsonObject EnumDefinition(EnumType type) {
        var members = new JsonArray();
        foreach (var member in type.MemberNames)
            members.Add(member);

        var definition = new JsonObject {
            ["type"] = "string",
            ["title"] = type.Name,
            ["enum"] = members,
        };
        ApplyTypeTerm(definition, type.Facets);
        if (MemberTerms(type) is { } memberTerms)
            definition["x-poly-enum"] = memberTerms;
        return definition;
    }

    /// <summary>
    /// The term keys for a member, in a stable order. Only members that declared a term
    /// appear; JSON Schema's <c>enum</c> stays the bare member list.
    /// </summary>
    private static JsonObject? MemberTerms(EnumType type) {
        var terms = new JsonObject();
        foreach (var member in type.MemberNames.OrderBy(m => m, StringComparer.Ordinal)) {
            if (!type.MemberFacets.TryGetValue(member, out var facets))
                continue;
            var term = TermFacets.Read(facets);
            if (term is null)
                continue;

            var entry = new JsonObject();
            if (term.Iri is { } iri) entry["iri"] = iri;
            if (term.Term is { } name) entry["term"] = name;
            if (term.Label is { } label) entry["label"] = label;
            if (term.Description is { } description) entry["description"] = description;
            if (entry.Count > 0)
                terms[member] = entry;
        }
        return terms.Count > 0 ? terms : null;
    }

    private static JsonObject ObjectDefinition(
        string name,
        IReadOnlyList<Facet> facets,
        IReadOnlyList<Property> properties,
        IReadOnlyList<Relationship> navigations,
        IReadOnlyDictionary<string, DomainType> byName,
        IReadOnlySet<string>? entryAssigned) {
        var shape = new JsonObject();
        var required = new JsonArray();

        foreach (var property in properties.OrderBy(p => p.Name, StringComparer.Ordinal)) {
            shape[property.Name] = PropertySchema(property, byName);
            if (DomainPresentation.IsRequired(property, entryAssigned))
                required.Add(property.Name);
        }

        foreach (var navigation in navigations.OrderBy(n => n.Name, StringComparer.Ordinal)) {
            // A schema object cannot carry both shapes under one key. Analysis is expected to
            // reject such a domain; if it did not, fail closed rather than silently drop one.
            if (shape.ContainsKey(navigation.Name))
                throw new InvalidOperationException(
                    $"Entity '{name}' declares both a property and a navigation named '{navigation.Name}'.");
            shape[navigation.Name] = NavigationSchema(navigation, byName);
        }

        var definition = new JsonObject {
            ["type"] = "object",
            ["title"] = name,
            ["properties"] = shape,
            ["additionalProperties"] = false,
        };
        ApplyTypeTerm(definition, facets);
        if (required.Count > 0)
            definition["required"] = required;
        return definition;
    }

    /// <summary>
    /// Projects an element's term onto its schema: the label becomes <c>title</c>, the
    /// description is the schema description, and the IRI is carried as <c>x-poly-iri</c>.
    /// The IRI is deliberately not the subschema <c>$id</c>: in draft 2020-12 a subschema
    /// <c>$id</c> starts a new embedded resource, so every local <c>#/$defs/…</c> reference
    /// inside the definition would resolve against the element IRI and dangle. The root
    /// <c>$id</c> (<c>urn:poly:&lt;domain&gt;</c>) stays the one base URI.
    /// </summary>
    private static void ApplyTypeTerm(JsonObject definition, IReadOnlyList<Facet> facets) {
        var term = TermFacets.Read(facets);
        if (term is null)
            return;
        if (term.Label is { } label)
            definition["title"] = label;
        if (term.Description is { } description)
            definition["description"] = description;
        if (term.Iri is { } iri)
            definition["x-poly-iri"] = iri;
    }

    private static void ApplyPropertyTerm(JsonObject schema, IReadOnlyList<Facet> facets) {
        var term = TermFacets.Read(facets);
        if (term is null)
            return;
        if (term.Label is { } label)
            schema["title"] = label;
        if (term.Description is { } description)
            schema["description"] = description;
        if (term.Iri is { } iri)
            schema["x-poly-iri"] = iri;
    }

    private static JsonObject PropertySchema(Property property, IReadOnlyDictionary<string, DomainType> byName) {
        var schema = TypeSchema(property.Type, byName);
        ApplyPropertyTerm(schema, property.Facets);
        var unknown = new JsonArray();

        foreach (var constraint in property.Constraints) {
            switch (constraint) {
                case RangeConstraint range:
                    AddBound(schema, "minimum", range.Minimum);
                    AddBound(schema, "maximum", range.Maximum);
                    break;
                case LengthConstraint length:
                    schema["minLength"] = length.MinLength;
                    schema["maxLength"] = length.MaxLength;
                    break;
                case PatternConstraint pattern:
                    schema["pattern"] = pattern.Pattern;
                    break;
                case EqualityConstraint equality:
                    schema["const"] = JsonSerializer.SerializeToNode(equality.ExpectedValue);
                    break;
                case DefaultValueConstraint defaultValue:
                    if (TryDefault(defaultValue, property, byName) is { } node)
                        schema["default"] = node;
                    break;
                case UniqueConstraint:
                    // JSON Schema validates one document; uniqueness across documents is a store fact.
                    schema["x-poly-unique"] = true;
                    break;
                case RequiredConstraint:
                case OptionalConstraint:
                    break;
                default:
                    // A constraint this exporter does not model is named, not dropped.
                    unknown.Add(constraint.GetType().Name);
                    break;
            }
        }

        if (unknown.Count > 0)
            schema["x-poly-constraints"] = unknown;
        return schema;
    }

    /// <summary>
    /// A static default is a literal, or an enum member written as <c>default(MemberName)</c>.
    /// Runtime defaults (<c>now</c>, <c>today</c>, <c>guid</c>) have no static JSON value and
    /// are omitted rather than guessed.
    /// </summary>
    private static JsonNode? TryDefault(
        DefaultValueConstraint constraint,
        Property property,
        IReadOnlyDictionary<string, DomainType> byName) {
        return constraint.Expression switch {
            Literal { Value: not null } literal => JsonSerializer.SerializeToNode(literal.Value),
            PropertyAccess access when byName.TryGetValue(property.Type.TypeName, out var declared)
                && declared is EnumType enumType
                && enumType.MemberNames.Contains(access.Name, StringComparer.Ordinal) =>
                    JsonValue.Create(access.Name),
            _ => null,
        };
    }

    private static JsonObject TypeSchema(DomainTypeReference reference, IReadOnlyDictionary<string, DomainType> byName) {
        if (!byName.TryGetValue(reference.TypeName, out var type))
            throw new InvalidOperationException(
                $"Property type '{reference.TypeName}' is not defined in the domain.");

        return type switch {
            PrimitiveType primitive => PrimitiveSchema(primitive.Semantics),
            _ => new JsonObject { ["$ref"] = $"#/$defs/{type.Name}" },
        };
    }

    private static JsonObject PrimitiveSchema(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => new JsonObject { ["type"] = "boolean" },
        PrimitiveSemantics.Number => new JsonObject { ["type"] = "integer" },
        PrimitiveSemantics.Decimal => new JsonObject { ["type"] = "number" },
        PrimitiveSemantics.Text => new JsonObject { ["type"] = "string" },
        PrimitiveSemantics.Uuid => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
        PrimitiveSemantics.Binary => new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" },
        PrimitiveSemantics.Date => new JsonObject { ["type"] = "string", ["format"] = "date" },
        PrimitiveSemantics.Time => new JsonObject { ["type"] = "string", ["format"] = "time" },
        PrimitiveSemantics.DateTime => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        PrimitiveSemantics.Duration => new JsonObject { ["type"] = "string", ["format"] = "duration" },
        _ => throw new InvalidOperationException($"Unsupported primitive semantics '{semantics}'."),
    };

    private static JsonObject NavigationSchema(
        Relationship navigation,
        IReadOnlyDictionary<string, DomainType> byName) {
        if (!byName.TryGetValue(navigation.Target.TypeName, out var targetType) || targetType is not Entity)
            throw new InvalidOperationException(
                $"Navigation target '{navigation.Target.TypeName}' is not an entity defined in the domain.");

        var target = new JsonObject { ["$ref"] = $"#/$defs/{targetType.Name}" };
        return navigation.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany
            ? new JsonObject { ["type"] = "array", ["items"] = target }
            : target;
    }

    private static void AddBound(JsonObject schema, string keyword, object? value) {
        if (value is null)
            return;

        var node = JsonSerializer.SerializeToNode(value);
        if (node is JsonValue jsonValue && jsonValue.GetValueKind() == JsonValueKind.Number)
            schema[keyword] = node;
        else
            schema["x-poly-" + keyword] = node;
    }
}