using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

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
}