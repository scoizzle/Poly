using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S11: a collection navigation whose generated C# member name (<c>PascalCase(nav)</c>)
/// equals its target entity's name shadows the type inside the emitted class. Lowering
/// emits <c>{TargetType}.Create(...)</c> for a create-in factory, which then binds to the
/// member (<c>IReadOnlyList&lt;T&gt;</c>) and the generated host does not compile.
/// </summary>
public sealed class NavNameShadowsEntityTypeTests {
    private static AnalysisResult Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(
            new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static IReadOnlyList<Diagnostic> AllErrors(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    [Test]
    public async Task CollectionNavNamedLikeTargetType_WithCreateIn_IsAnalyzeError() {
        var analysis = Analyze("""
            domain Col
            Claim: entity {
              Kind: Text
              evidence: many Evidence
              Attach: action (kind: Text) -> Evidence {
                create in evidence { Kind: kind }
              }
            }
            Evidence: entity {
              Kind: Text
              claim: Claim
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.NavigationShadowsEntityType);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(errors[0].Message).Contains("evidence")
            .And.Contains("Evidence")
            .And.Contains("shadows")
            .And.Contains("Rename the navigation")
            .And.Contains("global::Evidence.Create");
    }

    [Test]
    public async Task SingularNavNamedLikeTargetType_WithCreateIn_IsNotReported() {
        // A singular nav's property is `Target? Target`, so C#'s "Color Color" rule
        // resolves `Target.Create(...)` to the type — it compiles. Maintained probes
        // depend on this (Reservation.folio, Contact.account, Customer.address), so
        // only a collection nav (member type IReadOnlyList<T> ≠ member name) can break.
        var analysis = Analyze("""
            domain DocCreateInToOne
            Order: entity {
              Total: Number default(0)
              invoice: Invoice
              Finalize: action -> Invoice {
                assign Total to 100
                create in invoice { Amount: 100 }
              }
            }
            Invoice: entity {
              Amount: Number default(0)
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.NavigationShadowsEntityType)).IsEmpty();
        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task DifferingPluralNav_WithCreateIn_IsNotReported() {
        var analysis = Analyze("""
            domain Col
            Folder: entity {
              documents: many Document
              Attach: action (kind: Text) -> Document {
                create in documents { Kind: kind }
              }
            }
            Document: entity {
              Kind: Text
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.NavigationShadowsEntityType)).IsEmpty();
    }

    [Test]
    public async Task SameNameCollectionNav_WithoutCreateIn_IsNotReported() {
        // Decision: only a collection nav that lowering gives a create-in factory to can
        // shadow the type in the host — the emitted factory call is the sole
        // expression-position use of the target type name inside the class. A same-name
        // nav with no factory compiles, and this shape is pervasive in maintained probes
        // (`order: Order`, `customer: Customer`, `room: Room`, …), so reporting it would
        // break working domains for no benefit.
        var analysis = Analyze("""
            domain Col
            Claim: entity {
              Kind: Text
              evidence: many Evidence
            }
            Evidence: entity {
              Kind: Text
              claim: Claim
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.NavigationShadowsEntityType)).IsEmpty();
        await Assert.That(AllErrors(analysis)).IsEmpty();
    }
}