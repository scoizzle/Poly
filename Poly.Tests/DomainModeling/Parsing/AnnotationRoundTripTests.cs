using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;      // DomainDslPrinter (v1 domain-walk print)
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
// PolyDslParser


namespace Poly.Tests.DomainModeling.Parsing;

public class AnnotationRoundTripTests {
    private static DomainSession CreateTestContext() =>
        ExtensionCatalog.Core.Authoring;

    // Print path is unchanged v1 machinery (DomainDslPrinter walks the domain);
    // it needs the v1 annotation registry. Same SQL-pack handlers, v1 registry.
    private static AnnotationRegistry PrintAnnotations =>
        ExtensionCatalog.Core.Authoring.Annotations;

    [Test]
    public async Task ColumnAnnotation_ParsePrint_RoundTrips() {
        var poly = """
            domain Test

            Item: entity {
              Code: Text unique column("CODE")
              Name: Text column("NAME", "VARCHAR2(50)")
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var item = result.Root!.Types.OfType<Entity>().Single();
        var code = item.Properties.Single(p => p.Name == "Code");
        await Assert.That(code.Facets.Count).IsEqualTo(1);
        await Assert.That(code.Facets[0]).IsTypeOf<Annotation>();
        var ann = (Annotation)code.Facets[0];
        await Assert.That(ann.Name).IsEqualTo("column");
        await Assert.That(((AnnotationString)ann.Arguments["0"]).Value).IsEqualTo("CODE");

        var printer = new DomainDslPrinter(PrintAnnotations);
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("column(\"CODE\")")).IsTrue();
        await Assert.That(printed.Contains("column(\"NAME\", \"VARCHAR2(50)\")")).IsTrue();

        var parser2 = new PolyDslParser(printed, ctx);
        var changes2 = parser2.Parse();
        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        var item2 = result2.Root!.Types.OfType<Entity>().Single();
        await Assert.That(item2.Properties.All(p => p.Facets.Count == 1)).IsTrue();
    }

    [Test]
    public async Task TableAnnotation_EntityHeader_ParsePrint_RoundTrips() {
        var poly = """
            domain Test

            Order: entity table("ORDER_RECORDS") {
              Total: Number
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var order = result.Root!.Types.OfType<Entity>().Single();
        await Assert.That(order.Facets.Count).IsEqualTo(1);
        var ann = (Annotation)order.Facets[0];
        await Assert.That(ann.Name).IsEqualTo("table");
        await Assert.That(((AnnotationString)ann.Arguments["0"]).Value).IsEqualTo("ORDER_RECORDS");

        var printer = new DomainDslPrinter(PrintAnnotations);
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("table(\"ORDER_RECORDS\")")).IsTrue();

        var parser2 = new PolyDslParser(printed, ctx);
        var changes2 = parser2.Parse();
        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
        await Assert.That(result2.Root!.Types.OfType<Entity>().Single().Facets.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ColumnAndTable_Combined_RoundTrips() {
        var poly = """
            domain Test

            Patron: entity table("PATRON_MASTER") {
              CardNumber: Text unique column("CARD_NBR", "VARCHAR2(20)")
              Name: Text column("NAME")
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var changes = parser.Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var printer = new DomainDslPrinter(PrintAnnotations);
        var printed = printer.Print(result.Root);
        await Assert.That(printed.Contains("table(\"PATRON_MASTER\")")).IsTrue();
        await Assert.That(printed.Contains("column(\"CARD_NBR\", \"VARCHAR2(20)\")")).IsTrue();

        var parser2 = new PolyDslParser(printed, ctx);
        var changes2 = parser2.Parse();
        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes2);
        await Assert.That(result2.Succeeded).IsTrue();
    }

    [Test]
    public async Task UnknownAnnotation_WithoutPack_Fails() {
        var poly = """
            domain Test

            Item: entity {
              Code: Text column("CODE")
            }
            """;

        var parser = new PolyDslParser(poly);
        var ex = Assert.Throws<FormatException>(() => parser.Parse());
        await Assert.That(ex!.Message).Contains("unregistered annotation 'column'");
    }

    [Test]
    public async Task PrintWithoutPack_OfFacetedDomain_Fails() {
        var poly = """
            domain Test

            Item: entity {
              Code: Text column("CODE")
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(parser.Parse());
        await Assert.That(result.Succeeded).IsTrue();

        var printerNoFacets = new DomainDslPrinter();
        var ex = Assert.Throws<FormatException>(() => printerNoFacets.Print(result.Root!));
        await Assert.That(ex!.Message).Contains("no pack registered");
    }

    [Test]
    public async Task TableAnnotation_WithoutPack_Fails() {
        var poly = """
            domain Test

            Order: entity table("ORDERS") {
              Total: Number
            }
            """;

        var parser = new PolyDslParser(poly);
        var ex = Assert.Throws<FormatException>(() => parser.Parse());
        await Assert.That(ex!.Message).Contains("unregistered annotation 'table'");
    }

    [Test]
    public async Task EnumProperty_ColumnAnnotation_RoundTrips() {
        var poly = """
            domain Test

            Status: enum {
              Open,
              Closed,
            }

            Ticket: entity {
              State: Status column("STATE_CD")
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(parser.Parse());
        await Assert.That(result.Succeeded).IsTrue();

        var ticket = result.Root!.Types.OfType<Entity>().Single();
        var state = ticket.Properties.Single(p => p.Name == "State");
        await Assert.That(state.Facets.Count).IsEqualTo(1);
        await Assert.That(((Annotation)state.Facets[0]).Name).IsEqualTo("column");

        var printed = new DomainDslPrinter(PrintAnnotations).Print(result.Root);
        await Assert.That(printed.Contains("State: Status column(\"STATE_CD\")")).IsTrue();
    }

    [Test]
    public async Task TrailingComma_InAnnotationArgs_Fails() {
        var poly = """
            domain Test

            Item: entity {
              Code: Text column("CODE",)
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var ex = Assert.Throws<FormatException>(() => parser.Parse());
        await Assert.That(ex!.Message).Contains("Trailing comma");
    }

    [Test]
    public async Task ColumnAnnotation_EscapedQuotes_RoundTrip() {
        var poly = """
            domain Test

            Item: entity {
              Note: Text column("COL_\"X\"")
            }
            """;

        var ctx = CreateTestContext();
        var parser = new PolyDslParser(poly, ctx);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(parser.Parse());
        await Assert.That(result.Succeeded).IsTrue();

        var note = result.Root!.Types.OfType<Entity>().Single().Properties.Single();
        var ann = (Annotation)note.Facets[0];
        await Assert.That(((AnnotationString)ann.Arguments["0"]).Value).IsEqualTo("COL_\"X\"");

        var printed = new DomainDslPrinter(PrintAnnotations).Print(result.Root);
        await Assert.That(printed.Contains("column(\"COL_\\\"X\\\"\")")).IsTrue();

        var result2 = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(printed, ctx).Parse());
        await Assert.That(result2.Succeeded).IsTrue();
        var note2 = result2.Root!.Types.OfType<Entity>().Single().Properties.Single();
        await Assert.That(((AnnotationString)((Annotation)note2.Facets[0]).Arguments["0"]).Value)
            .IsEqualTo("COL_\"X\"");
    }

    [Test]
    public async Task LegacyActionAfterProperty_IsNotTreatedAsAnnotation() {
        // Core-only parse must still accept Name(params): action after a property.
        var poly = """
            domain Test

            Book: entity {
              Title: Text
              Checkout(days: Number): action {
              }
            }
            """;

        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        await Assert.That(result.Succeeded).IsTrue();
        var book = result.Root!.Types.OfType<Entity>().Single();
        await Assert.That(book.Actions.Count).IsEqualTo(1);
        await Assert.That(book.Actions[0].Name).IsEqualTo("Checkout");
    }

    [Test]
    public async Task Annotation_Equality_IsByContent() {
        var left = new Annotation("column", new Dictionary<string, AnnotationValue> {
            ["0"] = new AnnotationString("CODE"),
        });
        var right = new Annotation("column", new Dictionary<string, AnnotationValue> {
            ["0"] = new AnnotationString("CODE"),
        });

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task MalformedColumnFacet_Print_FailsClosed() {
        var domain = DomainTestFactory.Create("Test", [
            new Entity(
                "Item",
                [
                    new Property("Code", new DomainTypeReference("Text"), []) with {
                        Facets = [
                            new Annotation("column", new Dictionary<string, AnnotationValue> {
                                ["0"] = new AnnotationNumber(1),
                            })
                        ]
                    }
                ],
                [],
                [],
                [])
        ], []);

        var ctx = CreateTestContext();
        var ex = Assert.Throws<FormatException>(() => new DomainDslPrinter(PrintAnnotations).Print(domain));
        await Assert.That(ex!.Message).Contains("no pack registered");
    }

    [Test]
    public async Task ColumnAnnotation_Evolution_AddsFacet() {
        var ctx = CreateTestContext();
        var parser = new PolyDslParser("""
            domain Test
            Item: entity { Code: Text }
            """, ctx);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(parser.Parse());
        await Assert.That(result.Succeeded).IsTrue();

        var facetResult = new DomainEvolution(result.Root!).Apply([
            new AddFacetToPropertyChange("Item", "Code",
                new Annotation("column", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString("CODE")
                }))
        ]);
        await Assert.That(facetResult.Succeeded).IsTrue();
        var item = facetResult.Root!.Types.OfType<Entity>().Single();
        await Assert.That(item.Properties.Single(p => p.Name == "Code").Facets.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TableAnnotation_Evolution_AddsFacetToDomainType() {
        var ctx = CreateTestContext();
        var parser = new PolyDslParser("""
            domain Test
            Order: entity { Total: Number }
            """, ctx);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(parser.Parse());
        await Assert.That(result.Succeeded).IsTrue();

        var facetResult = new DomainEvolution(result.Root!).Apply([
            new AddFacetToDomainTypeChange("Order",
                new Annotation("table", new Dictionary<string, AnnotationValue> {
                    ["0"] = new AnnotationString("ORDERS")
                }))
        ]);
        await Assert.That(facetResult.Succeeded).IsTrue();
        await Assert.That(facetResult.Root!.Types.OfType<Entity>().Single().Facets.Count).IsEqualTo(1);
    }
}