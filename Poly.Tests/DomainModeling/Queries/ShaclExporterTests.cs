using System.Text.Json.Nodes;

using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// SHACL is the validation twin of the RDFS export: node shapes target the entity class,
/// property shapes carry the same constraints Poly already proved, and requiredness uses
/// the same create rule. The shapes must point at the same IRIs JSON-LD emits.
/// </summary>
public class ShaclExporterTests {
    private const string Catalog = """
        domain Catalog

        Color: enum iri("https://example.com/Color") {
          Red iri("https://example.com/Color/Red"),
          Green,
        }

        Line: entity {
          Sku: Text
        }

        Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
          Sku: Text iri("https://example.com/Product/sku") label("SKU") length(1, 20) pattern("^[A-Z0-9-]+$")
          Total: Number iri("https://example.com/Product/total") range(0, 100)
          State: Color
          Fixed: Text equals("x")
          Note: Text optional
          lines: many Line
          owner: Line
        }
        """;

    private static JsonNode Export(string poly) =>
        JsonNode.Parse(ShaclExporter.Export(EvolvedDomain.FromDsl(poly).Domain))!;

    private static JsonObject? NodeShape(JsonNode root, string iri) =>
        root["@graph"]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(n => n["@id"] is JsonValue id && id.GetValue<string>() == iri);

    private static JsonObject? PropertyShape(JsonNode root, string pathIri) {
        foreach (var node in root["@graph"]!.AsArray().OfType<JsonObject>()) {
            if (node["sh:property"] is not JsonArray properties)
                continue;
            foreach (var property in properties.OfType<JsonObject>()) {
                if (property["sh:path"] is JsonObject path
                    && path["@id"] is JsonValue id
                    && id.GetValue<string>() == pathIri) {
                    return property;
                }
            }
        }
        return null;
    }

    [Test]
    public async Task Export_EmitsNodeShapeTargetingTheEntityClass() {
        var root = Export(Catalog);
        var shape = NodeShape(root, "urn:poly:Catalog/shapes/Product")!;

        await Assert.That(shape["@type"]!.GetValue<string>()).IsEqualTo("sh:NodeShape");
        await Assert.That(shape["sh:targetClass"]!["@id"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Product");
        await Assert.That(shape["sh:closed"]!.GetValue<bool>()).IsTrue();
        await Assert.That(shape["sh:ignoredProperties"]!["@list"]!.AsArray()
            .Select(n => n!["@id"]!.GetValue<string>())).Contains("rdf:type")
            .Because("sh:ignoredProperties is an RDF list, not a set");
    }

    [Test]
    public async Task Export_MapsRangeLengthPatternAndEqualityToShaclKeywords() {
        var root = Export(Catalog);

        var sku = PropertyShape(root, "https://example.com/Product/sku")!;
        await Assert.That(sku["sh:datatype"]!["@id"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#string");
        await Assert.That(sku["sh:minLength"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(sku["sh:maxLength"]!.GetValue<int>()).IsEqualTo(20);
        await Assert.That(sku["sh:pattern"]!.GetValue<string>()).IsEqualTo("^[A-Z0-9-]+$");
        await Assert.That(sku["sh:name"]!.GetValue<string>()).IsEqualTo("SKU");

        var total = PropertyShape(root, "https://example.com/Product/total")!;
        await Assert.That(total["sh:minInclusive"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(total["sh:maxInclusive"]!.GetValue<int>()).IsEqualTo(100);

        var fixedValue = PropertyShape(root, "urn:poly:Catalog/Product/Fixed")!;
        var allowed = fixedValue["sh:in"]!["@list"]!.AsArray();
        await Assert.That(allowed.Count).IsEqualTo(1)
            .Because("sh:in with one value pins the property without requiring presence");
        await Assert.That(allowed[0]!["@value"]!.GetValue<string>()).IsEqualTo("x");
        await Assert.That(allowed[0]!["@type"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#string")
            .Because("RDF-term equality needs the literal datatype");
    }

    [Test]
    public async Task Export_RequirednessUsesTheCreateRule() {
        var root = Export(Catalog);

        var required = PropertyShape(root, "https://example.com/Product/sku")!;
        await Assert.That(required["sh:minCount"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(required["sh:maxCount"]!.GetValue<int>()).IsEqualTo(1);

        var optional = PropertyShape(root, "urn:poly:Catalog/Product/Note")!;
        await Assert.That(optional["sh:minCount"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(optional["sh:maxCount"]!.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Export_EnumPropertyGetsClassAndClosedSet() {
        var root = Export(Catalog);
        var state = PropertyShape(root, "urn:poly:Catalog/Product/State")!;

        await Assert.That(state["sh:class"]!["@id"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Color");
        var members = state["sh:in"]!["@list"]!.AsArray()
            .Select(n => n!["@id"]!.GetValue<string>()).ToList();
        await Assert.That(members).Contains("https://example.com/Color/Red");
        await Assert.That(members).Contains("https://example.com/Color/Green");
    }

    [Test]
    public async Task Export_NavigationBecomesPropertyShapeWithClass() {
        var root = Export(Catalog);

        var many = PropertyShape(root, "urn:poly:Catalog/Product/lines")!;
        await Assert.That(many["sh:class"]!["@id"]!.GetValue<string>())
            .IsEqualTo("urn:poly:Catalog/Line");
        await Assert.That(many["sh:maxCount"]).IsNull()
            .Because("a collection navigation is not capped at one");

        var single = PropertyShape(root, "urn:poly:Catalog/Product/owner")!;
        await Assert.That(single["sh:maxCount"]!.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Export_ValueTypeGetsShapeWithItsConstraints() {
        var root = Export("""
            domain MoneyDomain
            Money: value { Amount: Number range(0, 100) Currency: Text length(3, 3) }
            Holder: entity { Label: Text }
            """);

        var shape = NodeShape(root, "urn:poly:MoneyDomain/shapes/Money")!;
        await Assert.That(shape["sh:targetClass"]!["@id"]!.GetValue<string>())
            .IsEqualTo("urn:poly:MoneyDomain/Money");

        var amount = PropertyShape(root, "urn:poly:MoneyDomain/Money/Amount")!;
        await Assert.That(amount["sh:minInclusive"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(amount["sh:maxInclusive"]!.GetValue<int>()).IsEqualTo(100);
    }

    [Test]
    public async Task Export_UsesTheSameIrisAsTheJsonLdExport() {
        var (domain, _) = EvolvedDomain.FromDsl(Catalog);
        var shacl = JsonNode.Parse(ShaclExporter.Export(domain))!;
        var jsonLd = JsonNode.Parse(JsonLdExporter.Export(domain))!;

        var jsonLdIds = jsonLd["@graph"]!.AsArray().OfType<JsonObject>()
            .Select(n => n["@id"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var iri in ShaclIris(shacl))
            await Assert.That(jsonLdIds).Contains(iri);
    }

    private static IEnumerable<string> ShaclIris(JsonNode root) {
        foreach (var node in root["@graph"]!.AsArray().OfType<JsonObject>()) {
            if (node["sh:targetClass"] is JsonObject target
                && target["@id"] is JsonValue targetId) {
                yield return targetId.GetValue<string>();
            }
            if (node["sh:property"] is not JsonArray properties)
                continue;
            foreach (var property in properties.OfType<JsonObject>()) {
                if (property["sh:path"] is JsonObject path && path["@id"] is JsonValue pathId)
                    yield return pathId.GetValue<string>();
            }
        }
    }

    [Test]
    public async Task Export_IsDeterministic() {
        var first = ShaclExporter.Export(EvolvedDomain.FromDsl(Catalog).Domain);
        var second = ShaclExporter.Export(EvolvedDomain.FromDsl(Catalog).Domain);

        await Assert.That(second).IsEqualTo(first);
    }
}