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
/// #604 L-22b, owner ruling on #683: a public entity may navigate to an entity that is internal to the domain
/// (the navigation prints internal and the pack maps it explicitly). <c>DMCON019</c> now covers only an
/// entity-typed property of a public entity. An internal entity may navigate anywhere.
/// </summary>
public sealed class PublicNavigationClosureTests {
    private static IReadOnlyList<Diagnostic> AllErrors(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse())
            .Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

    private static IReadOnlyList<Diagnostic> Errors(string dsl) =>
        AllErrors(dsl).Where(d => d.Code == "DMCON019").ToList();

    [Test]
    public async Task Analyze_WhenPublicEntityHasManyNavToInternalEntity_ProducesNoDiagnostic() {
        var errors = AllErrors("""
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            public entity Order {
              Total: Number default(0)
              lines: many Line
            }
            """);

        await Assert.That(errors.Count(d => d.Code == "DMCON019")).IsEqualTo(0);
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Analyze_WhenPublicEntityHasSingularNavToInternalEntity_ProducesNoDiagnostic() {
        var errors = AllErrors("""
            domain Test

            Owner: entity {
              Name: Text
            }

            public entity Pet {
              Name: Text
              owner: Owner
            }
            """);

        await Assert.That(errors.Count(d => d.Code == "DMCON019")).IsEqualTo(0);
        await Assert.That(errors.Count).IsEqualTo(0);
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

    // F3 (fix round 1), reshaped by L-22b: with the navigation arm gone the stage group is reported only by the
    // group check, DMCON002, once, on the copy that returns the internal entity. Group coverage also lives in
    // PublicSurfaceClosureTests.PublicStageGroup_*.
    [Test]
    public async Task Analyze_WhenPublicStageGroupVariantReturnsInternalEntity_ReportsDMCON002OnTheCopy() {
        var errors = AllErrors("""
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

        await Assert.That(errors.Count(d => d.Code == "DMCON019")).IsEqualTo(0);
        var group = errors.Where(d => d.Code == "DMCON002").ToList();
        await Assert.That(group.Count).IsEqualTo(1);
        await Assert.That(group[0].Node is Poly.DomainModeling.Ontology.Action { Name: "Close" }).IsTrue();
        await Assert.That(group[0].Message).Contains("Receipt");
    }

    [Test]
    public async Task Analyze_WhenPublicEntityHasInternalActionReturningInternalEntity_IsClean() {
        await Assert.That(AllErrors("""
            domain Test

            Receipt: entity {
              Code: Text
            }

            public entity Order {
              receipts: many Receipt
              Close: action -> Receipt {
                create in receipts { Code: "x" }
              }
            }
            """)).IsEmpty();
    }

    // F4 (fix round 1), reshaped by L-22b: the navigation that used to be DMCON019 now compiles clean.
    [Test]
    public async Task Polyc_WhenPublicEntityNavigatesToInternalEntity_CompilesClean() {
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

        await Assert.That(result.Diagnostics.Count(d => d.Code == "DMCON019")).IsEqualTo(0);
        await Assert.That(result.Succeeded).IsTrue();
    }

    // F2 (fix round 1): a ManyToMany navigation from a public entity to an internal one is not expressible in the
    // DSL (model API only) and the pack's explicit mapping cannot prove a skip navigation over a non-public member, so it
    // fails closed under DMCON019, positioned on the navigation. A one-to-many navigation stays legal.
    [Test]
    public async Task Analyze_WhenPublicEntityHasManyToManyNavToInternalEntity_ReportsDMCON019OnTheNavigation() {
        var item = new Entity("Item", [], [], [], []);
        var order = new Entity("Order", [], [], [], []) with { Visibility = Visibility.Public };
        var navigation = new Relationship(
            "items", new DomainTypeReference("Order"), new DomainTypeReference("Item"),
            RelationshipCardinality.ManyToMany, []);
        var domain = DomainTestFactory.Create("T", [item, order], [navigation]);

        var errors = DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == "DMCON019")
            .ToList();

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is Relationship { Name: "items" }).IsTrue();
        await Assert.That(errors[0].Message).Contains("many-to-many");
    }

    [Test]
    public async Task Analyze_WhenInternalEntityHasManyToManyNavToInternalEntity_IsNotReported() {
        var item = new Entity("Item", [], [], [], []);
        var order = new Entity("Order", [], [], [], []);
        var navigation = new Relationship(
            "items", new DomainTypeReference("Order"), new DomainTypeReference("Item"),
            RelationshipCardinality.ManyToMany, []);
        var domain = DomainTestFactory.Create("T", [item, order], [navigation]);

        await Assert.That(DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Count(d => d.Code == "DMCON019")).IsEqualTo(0);
    }

    // P3: an enum has no visibility mark and is always public, so an enum-typed property on a public entity
    // is not a finding.
    [Test]
    public async Task Analyze_WhenPublicEntityPropertyIsEnum_IsClean() {
        await Assert.That(Errors("""
            domain Test

            Tier: enum { Gold, Silver }

            public entity Member {
              Level: Tier
            }
            """)).IsEmpty();
    }
}