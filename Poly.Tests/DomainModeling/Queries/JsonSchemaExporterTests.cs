using System.Text.Json.Nodes;

using Poly.DomainModeling.Queries;

namespace Poly.Tests.DomainModeling.Queries;

/// <summary>
/// JSON Schema is the first interop artifact agents and tooling ask for. It must project
/// the domain's own constraints (range, length, pattern, default, enum) rather than invent
/// a second meaning, and it must be deterministic so it can be diffed and regenerated.
/// </summary>
public class JsonSchemaExporterTests {
    private const string Shop = """
        domain Shop

        Color: enum { Red, Green, Blue }

        Line: entity {
          Sku: Text
        }

        Order: entity {
          Total: Number range(0, 100)
          Code: Text length(3, 8) pattern("^[A-Z]+$")
          Shade: Color default(Red)
          Note: Text optional
          lines: many Line
        }
        """;

    private static JsonNode Export(string poly) =>
        JsonNode.Parse(JsonSchemaExporter.Export(EvolvedDomain.FromDsl(poly).Domain))!;

    [Test]
    public async Task Export_EmitsDraft202012WithDefsPerNamedType() {
        var schema = Export(Shop);

        await Assert.That(schema["$schema"]!.GetValue<string>())
            .IsEqualTo("https://json-schema.org/draft/2020-12/schema");
        await Assert.That(schema["title"]!.GetValue<string>()).IsEqualTo("Shop");

        var defs = schema["$defs"]!;
        await Assert.That(defs["Order"]).IsNotNull();
        await Assert.That(defs["Line"]).IsNotNull();
        await Assert.That(defs["Color"]).IsNotNull();
    }

    [Test]
    public async Task Export_ProjectsRangeLengthAndPatternConstraints() {
        var properties = Export(Shop)["$defs"]!["Order"]!["properties"]!;

        await Assert.That(properties["Total"]!["type"]!.GetValue<string>()).IsEqualTo("integer");
        await Assert.That(properties["Total"]!["minimum"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(properties["Total"]!["maximum"]!.GetValue<int>()).IsEqualTo(100);

        await Assert.That(properties["Code"]!["type"]!.GetValue<string>()).IsEqualTo("string");
        await Assert.That(properties["Code"]!["minLength"]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(properties["Code"]!["maxLength"]!.GetValue<int>()).IsEqualTo(8);
        await Assert.That(properties["Code"]!["pattern"]!.GetValue<string>()).IsEqualTo("^[A-Z]+$");
    }

    [Test]
    public async Task Export_ProjectsEnumAsItsOwnDefinitionAndReference() {
        var schema = Export(Shop);
        var shade = schema["$defs"]!["Order"]!["properties"]!["Shade"]!;

        await Assert.That(shade["$ref"]!.GetValue<string>()).IsEqualTo("#/$defs/Color");
        await Assert.That(shade["default"]!.GetValue<string>()).IsEqualTo("Red");

        var members = schema["$defs"]!["Color"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        await Assert.That(members).Contains("Red");
        await Assert.That(members).Contains("Green");
        await Assert.That(members).Contains("Blue");
    }

    [Test]
    public async Task Export_RequiredFollowsPolyRules_DefaultAndOptionalAreOmittable() {
        var order = Export(Shop)["$defs"]!["Order"]!;
        var required = order["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

        await Assert.That(required).Contains("Total")
            .Because("a property is required at create unless it is optional");
        await Assert.That(required).Contains("Code");
        await Assert.That(required).DoesNotContain("Note")
            .Because("optional is the explicit opt-out");
        await Assert.That(required).DoesNotContain("Shade")
            .Because("a default supplies the value when the property is omitted");
    }

    [Test]
    public async Task Export_EmitsNavigationAsArrayOfReferences() {
        var lines = Export(Shop)["$defs"]!["Order"]!["properties"]!["lines"]!;

        await Assert.That(lines["type"]!.GetValue<string>()).IsEqualTo("array");
        await Assert.That(lines["items"]!["$ref"]!.GetValue<string>()).IsEqualTo("#/$defs/Line");
    }

    [Test]
    public async Task Export_ClosesObjectsAndListsRequiredProperties() {
        var line = Export(Shop)["$defs"]!["Line"]!;

        await Assert.That(line["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(line["additionalProperties"]!.GetValue<bool>()).IsFalse();
        await Assert.That(line["required"]).IsNotNull();
    }

    [Test]
    public async Task Export_EntryAssignedPropertyIsNotRequiredAtCreate() {
        // A first-stage entry effect initializes the property, so the Create factory has
        // no parameter for it and the schema must not demand it.
        var schema = Export("""
            domain Counter

            Counter: entity {
              Total: Number
              Frozen: Number
              Open: stage {
                entry { assign Frozen to 0 }
                Close: action { transition to Closed }
              }
              Closed: stage {}
            }
            """);

        var required = schema["$defs"]!["Counter"]!["required"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();
        await Assert.That(required).Contains("Total");
        await Assert.That(required).DoesNotContain("Frozen")
            .Because("the first stage's entry effect assigns it; it is not a create parameter");
    }

    [Test]
    public async Task Export_ProjectsEqualityUniqueValueTypeAndRuntimeDefault() {
        var schema = Export("""
            domain Extras
            uses temporal
            uses persistence

            Code: value {
              Value: Text
            }

            Thing: entity {
              Fixed: Text equals("x")
              Sku: Text unique
              Stamp: DateTime default(Now)
            }
            """);

        var thing = schema["$defs"]!["Thing"]!["properties"]!;
        await Assert.That(thing["Fixed"]!["const"]!.GetValue<string>()).IsEqualTo("x");
        await Assert.That(thing["Sku"]!["x-poly-unique"]!.GetValue<bool>()).IsTrue();
        await Assert.That(thing["Stamp"]!["default"]).IsNull()
            .Because("a runtime default has no static JSON value and is omitted, not guessed");

        await Assert.That(schema["$defs"]!["Code"]!["type"]!.GetValue<string>()).IsEqualTo("object");
        await Assert.That(schema["$defs"]!["Code"]!["properties"]!["Value"]!["type"]!.GetValue<string>())
            .IsEqualTo("string");
    }

    [Test]
    public async Task Export_PropertyAndNavigationWithSameName_FailsClosed() {
        var domain = new Poly.DomainModeling.Ontology.Domain("Clash", [
            new Poly.DomainModeling.Ontology.PrimitiveType(
                "Text", Poly.DomainModeling.Ontology.PrimitiveSemantics.Text, []),
            new Poly.DomainModeling.Ontology.Entity(
                "A",
                [new Poly.DomainModeling.Ontology.Property(
                    "items",
                    new Poly.DomainModeling.Ontology.DomainTypeReference("Text"),
                    [])],
                [], [], []) {
                Navigations = [new Poly.DomainModeling.Ontology.Relationship(
                    "items",
                    new Poly.DomainModeling.Ontology.DomainTypeReference("A"),
                    new Poly.DomainModeling.Ontology.DomainTypeReference("B"),
                    Poly.DomainModeling.Ontology.RelationshipCardinality.ManyToOne,
                    [])],
            },
            new Poly.DomainModeling.Ontology.Entity("B", [], [], [], []),
        ]);

        await Assert.That(() => JsonSchemaExporter.Export(domain))
            .Throws<InvalidOperationException>()
            .Because("one object cannot carry both shapes under one key; dropping either is silent loss");
    }

    [Test]
    public async Task Export_ProjectsEntityPropertyAndEnumMemberTermMetadata() {
        var schema = Export("""
            domain Catalog

            Color: enum {
              Red iri("https://example.com/Color/Red") term("red"),
              Green,
            }

            Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
              Sku: Text iri("https://example.com/Product/sku") label("SKU") description("Stock keeping unit") length(1, 20)
              Shade: Color
            }
            """);

        var product = schema["$defs"]!["Product"]!;
        await Assert.That(product["x-poly-iri"]!.GetValue<string>()).IsEqualTo("https://example.com/Product");
        await Assert.That(product["title"]!.GetValue<string>()).IsEqualTo("Product");
        await Assert.That(product["description"]!.GetValue<string>()).IsEqualTo("A sellable item");
        await Assert.That(product["$id"]).IsNull()
            .Because("a subschema $id starts an embedded resource and rebases local #/$defs refs");

        var sku = product["properties"]!["Sku"]!;
        await Assert.That(sku["x-poly-iri"]!.GetValue<string>()).IsEqualTo("https://example.com/Product/sku");
        await Assert.That(sku["title"]!.GetValue<string>()).IsEqualTo("SKU");
        await Assert.That(sku["description"]!.GetValue<string>()).IsEqualTo("Stock keeping unit");
        await Assert.That(sku["minLength"]!.GetValue<int>()).IsEqualTo(1)
            .Because("term metadata adds to the schema; it never replaces a constraint");

        var color = schema["$defs"]!["Color"]!;
        await Assert.That(color["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList())
            .IsEquivalentTo(new[] { "Red", "Green" });
        await Assert.That(color["x-poly-enum"]!["Red"]!["iri"]!.GetValue<string>())
            .IsEqualTo("https://example.com/Color/Red");
        await Assert.That(color["x-poly-enum"]!["Red"]!["term"]!.GetValue<string>()).IsEqualTo("red");
        await Assert.That(color["x-poly-enum"]!["Green"]).IsNull()
            .Because("a member with no term contributes no metadata");
    }

    [Test]
    public async Task Export_IriBearingDefinition_KeepsLocalRefsResolvable() {
        var schema = Export("""
            domain Catalog
            Color: enum { Red, Green }
            Product: entity iri("https://example.com/Product") {
              Shade: Color
            }
            """);

        var product = schema["$defs"]!["Product"]!;
        await Assert.That(product["$id"]).IsNull()
            .Because("a subschema $id would make #/$defs/Color resolve against the Product IRI");
        await Assert.That(product["properties"]!["Shade"]!["$ref"]!.GetValue<string>())
            .IsEqualTo("#/$defs/Color")
            .Because("the root document remains the only base URI");
    }

    [Test]
    public async Task Export_ProjectsEnumMemberLabelAndDescription() {
        var schema = Export("""
            domain Catalog
            Color: enum {
              Red iri("https://example.com/Color/Red") term("red") label("Red") description("The red member"),
              Green,
            }
            Product: entity { Shade: Color }
            """);

        var red = schema["$defs"]!["Color"]!["x-poly-enum"]!["Red"]!;
        await Assert.That(red["label"]!.GetValue<string>()).IsEqualTo("Red");
        await Assert.That(red["description"]!.GetValue<string>()).IsEqualTo("The red member");
    }

    [Test]
    public async Task Export_IsDeterministic() {
        var first = JsonSchemaExporter.Export(EvolvedDomain.FromDsl(Shop).Domain);
        var second = JsonSchemaExporter.Export(EvolvedDomain.FromDsl(Shop).Domain);

        await Assert.That(second).IsEqualTo(first);
    }
}