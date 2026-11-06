using System.Text.Json.Nodes;

using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// JSON-LD is the semantic-web door: an <c>@context</c> plus an RDFS graph. Declared
/// term IRIs must be used as written; an untagged element gets a stable <c>urn:poly:</c>
/// identifier rather than being dropped from the graph.
/// </summary>
public class JsonLdExporterTests {
    private const string Catalog = """
        domain Catalog

        Color: enum {
          Red iri("https://example.com/Color/Red") term("red"),
          Green,
        }

        Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
          Sku: Text iri("https://example.com/Product/sku") label("SKU") length(1, 20)
          Shade: Color
          Discontinued: Boolean
          ReleasedOn: Date
        }
        """;

    private static JsonNode Export(string poly) =>
        JsonNode.Parse(JsonLdExporter.Export(EvolvedDomain.FromDsl(poly).Domain))!;

    private static JsonArray Graph(JsonNode root) => root["@graph"]!.AsArray();

    private static JsonObject? Node(JsonNode root, string iri) =>
        Graph(root).OfType<JsonObject>().SingleOrDefault(n =>
            n["@id"] is JsonValue id && id.GetValue<string>() == iri);

    [Test]
    public async Task Export_EmitsContextWithStandardPrefixesAndTermIris() {
        var root = Export(Catalog);
        var context = root["@context"]!;

        await Assert.That(context["rdf"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        await Assert.That(context["rdfs"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2000/01/rdf-schema#");
        await Assert.That(context["xsd"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#");
        await Assert.That(context["Product"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Product");
        await Assert.That(context["Sku"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Product/sku");
    }

    [Test]
    public async Task Export_EmitsClassNodeWithLabelAndDescription() {
        var root = Export(Catalog);
        var product = Node(root, "https://example.com/Product")!;

        await Assert.That(product["@type"]!.GetValue<string>()).IsEqualTo("rdfs:Class");
        await Assert.That(product["rdfs:label"]!.GetValue<string>()).IsEqualTo("Product");
        await Assert.That(product["rdfs:comment"]!.GetValue<string>()).IsEqualTo("A sellable item");
    }

    [Test]
    public async Task Export_EmitsPropertyNodeWithDomainAndXsdRange() {
        var root = Export(Catalog);

        var sku = Node(root, "https://example.com/Product/sku")!;
        await Assert.That(sku["@type"]!.GetValue<string>()).IsEqualTo("rdf:Property");
        await Assert.That(sku["rdfs:domain"]!["@id"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Product");
        await Assert.That(sku["rdfs:range"]!["@id"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#string");
        await Assert.That(sku["rdfs:label"]!.GetValue<string>()).IsEqualTo("SKU");

        var discontinued = Node(root, "urn:poly:Catalog/Product/Discontinued")!;
        await Assert.That(discontinued["rdfs:range"]!["@id"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#boolean");
        var released = Node(root, "urn:poly:Catalog/Product/ReleasedOn")!;
        await Assert.That(released["rdfs:range"]!["@id"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/2001/XMLSchema#date");
    }

    [Test]
    public async Task Export_EmitsEnumClassAndMemberNodes() {
        var root = Export(Catalog);

        var color = Node(root, "urn:poly:Catalog/Color")!;
        await Assert.That(color["@type"]!.GetValue<string>()).IsEqualTo("rdfs:Class");

        var red = Node(root, "https://example.com/Color/Red")!;
        await Assert.That(red["@type"]!.GetValue<string>()).IsEqualTo("urn:poly:Catalog/Color");
        await Assert.That(red["rdfs:label"]!.GetValue<string>()).IsEqualTo("Red");

        var green = Node(root, "urn:poly:Catalog/Color/Green")!;
        await Assert.That(green["@type"]!.GetValue<string>()).IsEqualTo("urn:poly:Catalog/Color");
    }

    [Test]
    public async Task Export_EmitsNavigationAsPropertyWithEntityRange() {
        var root = Export("""
            domain Shop
            Line: entity { Sku: Text }
            Order: entity { lines: many Line }
            """);

        var nav = Node(root, "urn:poly:Shop/Order/lines")!;
        await Assert.That(nav["@type"]!.GetValue<string>()).IsEqualTo("rdf:Property");
        await Assert.That(nav["rdfs:domain"]!["@id"]!.GetValue<string>())
            .IsEqualTo("urn:poly:Shop/Order");
        await Assert.That(nav["rdfs:range"]!["@id"]!.GetValue<string>())
            .IsEqualTo("urn:poly:Shop/Line");
    }

    [Test]
    public async Task Export_SynthesizesStableIrisForUntaggedElements() {
        var root = Export("""
            domain Bare
            Item: entity { Name: Text }
            """);

        await Assert.That(Node(root, "urn:poly:Bare/Item")).IsNotNull();
        await Assert.That(Node(root, "urn:poly:Bare/Item/Name")).IsNotNull();
    }

    [Test]
    public async Task Export_RepeatedMemberName_IsQualifiedAndKeepsBothIris() {
        var root = Export("""
            domain Dup
            A: entity { Sku: Text iri("https://example.com/A/sku") }
            B: entity { Sku: Text iri("https://example.com/B/sku") }
            """);

        await Assert.That(root["@context"]!["A.Sku"]!.GetValue<string>())
            .IsEqualTo("https://example.com/A/sku");
        await Assert.That(root["@context"]!["B.Sku"]!.GetValue<string>())
            .IsEqualTo("https://example.com/B/sku");
        await Assert.That(root["@context"]!["Sku"]).IsNull();
    }

    [Test]
    public async Task Export_TypeNamedLikeAPrefix_DoesNotShadowIt() {
        var root = Export("""
            domain Shadow
            rdf: entity { Name: Text }
            """);

        await Assert.That(root["@context"]!["rdf"]!.GetValue<string>())
            .IsEqualTo("http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        await Assert.That(Node(root, "urn:poly:Shadow/rdf")).IsNotNull();
    }

    [Test]
    public async Task Export_EnumMemberWithTermOnly_UsesTheVocabularyTerm() {
        var root = Export("""
            domain Colorful
            Color: enum iri("https://example.com/color/") { Red term("red"), }
            """);

        await Assert.That(Node(root, "https://example.com/color/red")).IsNotNull();
    }

    [Test]
    public async Task Export_EnumMemberHonoursFragmentBase() {
        var root = Export("""
            domain Colorful
            Color: enum iri("https://example.com/color#") { Red, }
            """);

        await Assert.That(Node(root, "https://example.com/color#Red")).IsNotNull();
    }

    [Test]
    public async Task Export_IsDeterministic() {
        var first = JsonLdExporter.Export(EvolvedDomain.FromDsl(Catalog).Domain);
        var second = JsonLdExporter.Export(EvolvedDomain.FromDsl(Catalog).Domain);

        await Assert.That(second).IsEqualTo(first);
    }
}