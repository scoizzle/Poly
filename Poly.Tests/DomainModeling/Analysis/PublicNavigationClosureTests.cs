using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #604 L-22, ruling on #683: a public entity may not navigate to an entity that is internal to the domain
/// (<c>DMCON019</c>). EF maps only public navigations, so demoting the printed navigation is not an option;
/// the fix is to mark the target public. An internal entity may navigate anywhere.
/// </summary>
public sealed class PublicNavigationClosureTests {
    private static IReadOnlyList<Diagnostic> Errors(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse())
            .Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == "DMCON019")
            .ToList();

    [Test]
    public async Task Analyze_WhenPublicEntityHasManyNavToInternalEntity_ReportsDMCON019() {
        var errors = Errors("""
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            public entity Order {
              Total: Number default(0)
              lines: many Line
            }
            """);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Order");
        await Assert.That(errors[0].Node is Relationship { Name: "lines" }).IsTrue();
        await Assert.That(errors[0].Message).Contains("lines");
        await Assert.That(errors[0].Message).Contains("Line");
        await Assert.That(errors[0].Message).Contains("public");
    }

    [Test]
    public async Task Analyze_WhenPublicEntityHasSingularNavToInternalEntity_ReportsDMCON019() {
        var errors = Errors("""
            domain Test

            Owner: entity {
              Name: Text
            }

            public entity Pet {
              Name: Text
              owner: Owner
            }
            """);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("owner");
        await Assert.That(errors[0].Node is Relationship { Name: "owner" }).IsTrue();
    }

    [Test]
    public async Task Analyze_WhenInternalEntityNavigatesToInternalEntity_IsClean() {
        await Assert.That(Errors("""
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            Order: entity {
              lines: many Line
            }
            """)).IsEmpty();
    }

    [Test]
    public async Task Analyze_WhenPublicEntityNavigatesToPublicEntity_IsClean() {
        await Assert.That(Errors("""
            domain Test

            public entity Line {
              Qty: Number default(1)
            }

            public entity Order {
              lines: many Line
            }
            """)).IsEmpty();
    }

    [Test]
    public async Task Analyze_WhenInternalEntityNavigatesToPublicEntity_IsClean() {
        await Assert.That(Errors("""
            domain Test

            public entity Line {
              Qty: Number default(1)
            }

            Order: entity {
              lines: many Line
            }
            """)).IsEmpty();
    }

    // R3 (#683 ruling): a public entity member of a value type cannot reach the printer. Stored state typed by
    // a domain value type is DMSTR006 for any visibility, so DomainAccess.Demote's value-type arm never turns a
    // persisted property internal (no silent-loss shape exists).
    [Test]
    [Arguments("Money: value {")]
    [Arguments("public value Money {")]
    public async Task Analyze_WhenPublicEntityPropertyIsValueType_IsRejectedAsDMSTR006(string valueHeader) {
        var diagnostics = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser($$"""
                domain Test

                {{valueHeader}}
                  Amount: Number
                }

                public entity Order {
                  Price: Money
                }
                """).Parse())
            .Analysis.Diagnostics;

        await Assert.That(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == "DMSTR006")).IsTrue();
    }

    // F2 (fix round 1): an entity-typed PROPERTY built through the model API (the DSL reads one as a
    // navigation) reached the printer as CS0053. DMCON019 covers it, positioned on the property.
    [Test]
    public async Task Analyze_WhenPublicEntityHasEntityTypedProperty_ReportsDMCON019OnTheProperty() {
        var owner = new Entity("Owner", [], [], [], []);
        var pet = new Entity("Pet", [new Property("Owner", new DomainTypeReference("Owner"), [])], [], [], [])
            with { Visibility = Visibility.Public };
        var errors = DomainModelAnalyzer.Analyze(new Domain("T", [owner, pet])).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == "DMCON019")
            .ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is Property { Name: "Owner" }).IsTrue();
        await Assert.That(errors[0].Message).Contains("property 'Owner'");
    }

    // F3 (fix round 1): a public stage group cannot carry an internal-entity result in any variant. A typed
    // result is produced by create-in, which needs a navigation, so DMCON019 rejects the public owner.
    [Test]
    public async Task Analyze_WhenPublicStageGroupVariantReturnsInternalEntity_ReportsDMCON019() {
        var errors = Errors("""
            domain Test

            Receipt: entity {
              Code: Text
            }

            public entity Order {
              receipts: many Receipt
              A: stage {
                public action Close { transition to B }
              }
              B: stage {
                Close: action -> Receipt {
                  create in receipts { Code: "x" }
                }
              }
            }
            """);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("receipts");
    }

    // F4 (fix round 1): polyc places DMCON019 on the navigation's line.
    [Test]
    public async Task Polyc_ReportsDMCON019PlacedAtTheNavigation() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            public entity Order {
              Total: Number default(0)
              lines: many Line
            }
            """;

        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(Dsl, "n.poly");

        var errors = result.Diagnostics.Where(d => d.Code == "DMCON019").ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Placed).IsTrue();
        await Assert.That(errors[0].File).IsEqualTo("n.poly");
        await Assert.That(errors[0].Line).IsEqualTo(9);
        await Assert.That(result.Succeeded).IsFalse();
    }
}