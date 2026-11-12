using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// S-11 (Q10): <c>via</c> stays verbatim in the IR (F-15); this file pins the analysis
/// that reads it. A <c>via</c> naming nothing is <c>DMREL003</c>; two sides that
/// contradict each other are <c>DMREL004</c>; an undeclared pairing with more than one
/// candidate is <c>DMREL005</c>. All three report on the navigation node.
/// </summary>
public sealed class InverseDeclarationTests {
    private static AnalysisResult Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static IReadOnlyList<Diagnostic> AllErrors(AnalysisResult analysis) =>
        analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    [Test]
    public async Task UnknownInverse_Error() {
        var analysis = Analyze("""
            domain UnknownInverse
            Department: entity {
              reports: many Employee via manager
            }
            Employee: entity {
              Name: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseUnknown);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node).IsTypeOf<Poly.DomainModeling.Ontology.Relationship>();
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("reports");
        await Assert.That(errors[0].Message).Contains("manager");
        await Assert.That(errors[0].Message).Contains("Employee");
    }

    [Test]
    public async Task UnknownInverse_NamingAProperty_Error() {
        // A property of the named name is not a navigation: the clause names the
        // navigation that points back, so this is still DMREL003.
        var analysis = Analyze("""
            domain UnknownInverseProperty
            Department: entity {
              reports: many Employee via manager
            }
            Employee: entity {
              manager: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseUnknown);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("manager");
    }

    [Test]
    public async Task NonReciprocalPair_Error() {
        // A.x names B.y, but B.y names A.x2: the two sides contradict each other.
        var analysis = Analyze("""
            domain NonReciprocal
            A: entity {
              x: B via y
              x2: B
            }
            B: entity {
              y: A via x2
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("x");
        await Assert.That(errors[0].Message).Contains("x2");
    }

    [Test]
    public async Task InverseTargetsAnotherEntity_Error() {
        var analysis = Analyze("""
            domain WrongDirection
            A: entity {
              x: B via y
            }
            B: entity {
              y: C
            }
            C: entity {
              Name: Text
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("y");
        await Assert.That(errors[0].Message).Contains("C");
    }

    [Test]
    public async Task OwnershipDirectionDisagrees_Error() {
        // `owned` + `via` is legal; both ends owned is not — exactly one side owns.
        var analysis = Analyze("""
            domain BothOwned
            Line: entity {
              sku: Text
              order: owned Order via lines
            }
            Order: entity {
              lines: many owned Line via order
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("owned");
    }

    [Test]
    public async Task SelfNamingInverse_Error() {
        var analysis = Analyze("""
            domain SelfInverse
            Account: entity {
              Name: Text
              parent: Account via parent
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("parent");
    }

    [Test]
    public async Task TwoNavigationsClaimTheSameUndeclaredInverse_Error() {
        // Both navigations name `peer`, and `Child.peer` declares no inverse:
        // at most one navigation may claim an undeclared peer.
        var analysis = Analyze("""
            domain TwoClaims
            Parent: entity {
              first: Child via peer
              second: Child via peer
            }
            Child: entity {
              peer: Parent
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("second");
    }

    [Test]
    public async Task AmbiguousUndeclaredPairing_Error() {
        var analysis = Analyze("""
            domain AmbiguousPairing
            Peer: entity {
              primary: many Child
              secondary: many Child
            }
            Child: entity {
              peer: Peer
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseAmbiguous);
        await Assert.That(errors.Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("peer");
        await Assert.That(errors[0].Message).Contains("primary");
        await Assert.That(errors[0].Message).Contains("secondary");
    }

    [Test]
    public async Task AmbiguousOwnedPairing_IsNotDMREL005() {
        // S-12's DMSTR010 shape: two owned candidates belong to the ownership rule.
        var analysis = Analyze("""
            domain TwoOwnedCandidates
            Peer: entity {
              primary: many owned Child
              secondary: many owned Child
            }
            Child: entity {
              peer: Peer
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseAmbiguous)).IsEmpty();
    }

    [Test]
    public async Task OneOwnedCandidate_IsAmbiguous() {
        // One owned candidate is not the two-owner shape: the pairing is still
        // ambiguous, and S-12 does not own it.
        var analysis = Analyze("""
            domain OneOwnedCandidate
            Peer: entity {
              primary: many owned Child
              secondary: many Child
            }
            Child: entity {
              peer: Peer
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseAmbiguous);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("primary");
    }

    [Test]
    public async Task DeclaredReciprocalPair_IsClean() {
        var analysis = Analyze("""
            domain Reciprocal
            Department: entity {
              reports: many Employee via manager
            }
            Employee: entity {
              manager: Department via reports
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task OneSidedVia_IsClean() {
        // Q10: the sides are independent — one may declare the other's absence.
        var analysis = Analyze("""
            domain OneSided
            Department: entity {
              reports: many Employee via manager
            }
            Employee: entity {
              manager: Department
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task OwnedReciprocalPair_IsClean() {
        var analysis = Analyze("""
            domain OwnedPair
            Line: entity {
              sku: Text
              order: Order via lines
            }
            Order: entity {
              lines: many owned Line via order
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task MixedCardinalityPair_IsClean() {
        // #584: one singular and one collection on the same pair are not an
        // ambiguity; only two navigations of the same cardinality class are.
        var analysis = Analyze("""
            domain MixedCardinality
            Person: entity {
              referrer: Person
              friends: many Person
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }
}