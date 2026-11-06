using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// Ontology term metadata (IRI, label, description, enum term) is authored in `.poly`,
/// survives printing, and is reachable from code as a typed <see cref="TermMetadata"/>.
/// Without it the JSON Schema artifact is schema-shaped but not linked to an ontology.
/// </summary>
public class TermAnnotationTests {
    private const string Dsl = """
        domain Catalog

        Color: enum iri("https://example.com/Color") label("Color") {
          Red iri("https://example.com/Color/Red") term("red"),
          Green,
        }

        Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
          Sku: Text iri("https://example.com/Product/sku") label("SKU") description("Stock keeping unit")
          Shade: Color
        }
        """;

    [Test]
    public async Task Parse_EntityPropertyAndEnumMemberTerms_AreKept() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);

        var product = domain.Types.OfType<Entity>().Single(e => e.Name == "Product");
        var entityTerm = TermFacets.Read(product.Facets);
        await Assert.That(entityTerm).IsNotNull();
        await Assert.That(entityTerm!.Iri).IsEqualTo("https://example.com/Product");
        await Assert.That(entityTerm.Label).IsEqualTo("Product");
        await Assert.That(entityTerm.Description).IsEqualTo("A sellable item");

        var sku = product.Properties.Single(p => p.Name == "Sku");
        var propertyTerm = TermFacets.Read(sku.Facets);
        await Assert.That(propertyTerm).IsNotNull();
        await Assert.That(propertyTerm!.Iri).IsEqualTo("https://example.com/Product/sku");
        await Assert.That(propertyTerm.Label).IsEqualTo("SKU");
        await Assert.That(propertyTerm.Description).IsEqualTo("Stock keeping unit");

        var color = domain.Types.OfType<EnumType>().Single();
        var colorTerm = TermFacets.Read(color.Facets);
        await Assert.That(colorTerm).IsNotNull();
        await Assert.That(colorTerm!.Iri).IsEqualTo("https://example.com/Color");
        await Assert.That(colorTerm.Label).IsEqualTo("Color");
        await Assert.That(color.MemberNames).Contains("Red");
        await Assert.That(color.MemberFacets.ContainsKey("Red")).IsTrue();
        var red = TermFacets.Read(color.MemberFacets["Red"]);
        await Assert.That(red).IsNotNull();
        await Assert.That(red!.Iri).IsEqualTo("https://example.com/Color/Red");
        await Assert.That(red.Term).IsEqualTo("red");
        await Assert.That(color.MemberFacets.ContainsKey("Green")).IsFalse()
            .Because("Green declared no term");
    }

    [Test]
    public async Task Print_RoundTripsTermAnnotations() {
        var (domain, _) = EvolvedDomain.FromDsl(Dsl);
        var printed = new DomainDslPrinter().Print(domain);

        await Assert.That(printed).Contains("iri(\"https://example.com/Product\")");
        await Assert.That(printed).Contains("label(\"SKU\")");
        await Assert.That(printed).Contains("Color: enum iri(\"https://example.com/Color\") label(\"Color\") {");
        await Assert.That(printed).Contains("Red iri(\"https://example.com/Color/Red\") term(\"red\")");

        var (reparsed, _) = EvolvedDomain.FromDsl(printed);
        await Assert.That(new DomainDslPrinter().Print(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task Evolution_MalformedTermOnInitialProperty_FailsClosed() {
        var bad = new Property("X", new DomainTypeReference("Text"), []) {
            Facets = [new Annotation("iri", new Dictionary<string, AnnotationValue> {
                ["0"] = new AnnotationNumber(1),
            })],
        };

        await Assert.That(() => new DomainEvolution(new Domain("T", []))
                .Apply([new AddEntityChange("A", [bad])]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Evolution_MalformedTermFromCode_FailsClosed() {
        var malformed = new AddEnumTypeChange("Color", ["Red"], new Dictionary<string, IReadOnlyList<Facet>> {
            ["Red"] = [new Annotation("iri", new Dictionary<string, AnnotationValue> {
                ["0"] = new AnnotationNumber(1),
            })],
        });

        await Assert.That(() => new DomainEvolution(new Domain("T", [])).Apply([malformed]))
            .Throws<InvalidOperationException>()
            .Because("the DSL is not the only way into the model; the change surface validates too");
    }

    [Test]
    public async Task Evolution_TermForUnknownEnumMember_FailsClosed() {
        var stray = new AddEnumTypeChange("Color", ["Red"], new Dictionary<string, IReadOnlyList<Facet>> {
            ["Green"] = [new Annotation("iri", new Dictionary<string, AnnotationValue> {
                ["0"] = new AnnotationString("https://example.com/Green"),
            })],
        });

        await Assert.That(() => new DomainEvolution(new Domain("T", [])).Apply([stray]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("domain T\nA: entity iri(42) { X: Text }")]
    [Arguments("domain T\nA: entity iri(\"\") { X: Text }")]
    [Arguments("domain T\nA: entity iri(\"  \") { X: Text }")]
    [Arguments("domain T\nA: entity label(true) { X: Text }")]
    [Arguments("domain T\nA: entity description() { X: Text }")]
    [Arguments("domain T\nA: entity iri(\"https://a.example/x\", \"https://b.example/x\") { X: Text }")]
    [Arguments("domain T\nA: entity iri(\"schema:Person\") { X: Text }")]
    [Arguments("domain T\nA: entity iri(\"../relative\") { X: Text }")]
    [Arguments("domain T\nA: entity { X: Text label(null) }")]
    [Arguments("domain T\nColor: enum { Red iri(1), }")]
    [Arguments("domain T\nColor: enum { Red iri(\"https://a.example/x\") iri(\"https://b.example/x\"), }")]
    [Arguments("domain T\nColor: enum { Red table(\"R\"), }")]
    public async Task Parse_MalformedTerm_FailsClosed(string poly) {
        await Assert.That(() => new PolyDslParser(poly).Parse())
            .Throws<FormatException>()
            .Because("a typo in term metadata must fail at its position, not become a silent term");
    }

    [Test]
    public async Task Parse_TermOutsideAnEnumMember_FailsClosed() {
        await Assert.That(() => new PolyDslParser(
                "domain T\nA: entity term(\"a\") { X: Text }").Parse())
            .Throws<FormatException>();
        await Assert.That(() => new PolyDslParser(
                "domain T\nA: entity { X: Text term(\"x\") }").Parse())
            .Throws<FormatException>();
    }

    [Test]
    public async Task Parse_DuplicateTermKeyOnOneTarget_FailsClosed() {
        await Assert.That(() => new PolyDslParser(
                "domain T\nA: entity iri(\"https://a.example/x\") iri(\"https://b.example/x\") { X: Text }").Parse())
            .Throws<FormatException>()
            .Because("a target has exactly one IRI; the second is a typo");
    }
}