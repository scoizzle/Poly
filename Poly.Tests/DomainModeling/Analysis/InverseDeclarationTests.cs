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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        // V-14 flip: AllErrors was 1. Both ends owned is also a Line <-> Order ownership
        // ring, so it is also one DMDEP002 — 1 DMREL004 + 1 DMDEP002.
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnershipCycle).Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(2);
        await Assert.That(errors[0].Message).Contains("owned");
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("order");
        await Assert.That(nav.Source.TypeName).IsEqualTo("Line");
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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        // S-12: the two owned candidates are Peer.primary and Peer.secondary owning one Child.
        var owners = Errors(analysis, DomainModelDiagnosticCodes.OwnedTargetMultipleOwners);
        await Assert.That(owners.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
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
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("primary");
    }

    [Test]
    public async Task TwoBothOwnedPairings_SameEntities_ReportTwice() {
        // The both-owned dedupe is per navigation pair, not per entity pair: two distinct
        // owned pairings between the same two entities are two contradictions.
        var analysis = Analyze("""
            domain TwoOwnedPairs
            Line: entity {
              sku: Text
              order: owned Order via lines
              owner: owned Order via line
            }
            Order: entity {
              lines: many owned Line via order
              line: owned Line via owner
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(2);
        // S-12 flip: AllErrors was 2. Order and Line each now have two owner navigations,
        // so each is also DMSTR010 — 2 DMREL004 + 2 DMSTR010.
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnedTargetMultipleOwners).Count).IsEqualTo(2);
        // V-14 flip: AllErrors was 4. The Line <-> Order ring is one cycle however many
        // navigations close it: + 1 DMDEP002.
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnershipCycle).Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(5);
        var names = errors
            .Select(d => ((Poly.DomainModeling.Ontology.Relationship)d.Node).Name)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(names).IsEquivalentTo(["order", "owner"]);
    }

    [Test]
    public async Task BothOwned_OneSidedVia_ReportsOnce() {
        // Guard row (rule 6b): only one end names the pairing; the pair is still both-owned
        // and DMREL004 reports it once, from the side that declares `via`; DMDEP002 adds the
        // ring (V-14) as a separate error.
        var analysis = Analyze("""
            domain OneSidedBothOwned
            Line: entity {
              sku: Text
              order: owned Order
            }
            Order: entity {
              lines: many owned Line via order
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        // V-14 flip: AllErrors was 1; the both-owned pair is also one DMDEP002.
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnershipCycle).Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(2);
        await Assert.That(((Poly.DomainModeling.Ontology.Relationship)errors[0].Node).Name).IsEqualTo("lines");
    }

    [Test]
    public async Task BothOwnedWithSecondClaimer_PinsTheMeasuredCount() {
        // Characterization pin, not deduped (S-12 F4): one root cause — Order owns Line
        // twice while Line.order also owns Order — is reported as it is found: 3 DMREL004
        // (two both-owned pairings and the duplicate `via order` claim) and 1 DMSTR010.
        var analysis = Analyze("""
            domain BothOwnedSecondClaimer
            Line: entity {
              sku: Text
              order: owned Order
            }
            Order: entity {
              lines: many owned Line via order
              line: owned Line via order
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory).Count).IsEqualTo(3);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnedTargetMultipleOwners).Count).IsEqualTo(1);
        // V-14 flip: AllErrors was 4; the Line <-> Order ring is one more, DMDEP002.
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.OwnershipCycle).Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(5);
    }

    [Test]
    public async Task DeclaredPairingInOtherClass_DoesNotSuppressAmbiguity() {
        // The declared-side skip is per cardinality class: the two declared collection
        // navigations pair with r1/r2 and say nothing about the two undeclared singulars.
        var analysis = Analyze("""
            domain OtherClassDeclared
            A: entity {
              c1: many B via r1
              c2: many B via r2
              s1: B
              s2: B
            }
            B: entity {
              r1: A via c1
              r2: A via c2
              back: A
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseAmbiguous);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("back");
        await Assert.That(errors[0].Message).Contains("'s1'");
        await Assert.That(errors[0].Message).Contains("'s2'");
        await Assert.That(errors[0].Message).DoesNotContain("'c1'");
    }

    [Test]
    public async Task DeclaredReverse_SuppressesAmbiguity() {
        // The other half of the suppression: the navigation that points back declares
        // `via` into the ambiguous class, so the pairing is declared on that side.
        var analysis = Analyze("""
            domain DeclaredReverse
            Peer: entity {
              primary: many Child
              secondary: many Child
            }
            Child: entity {
              peer: Peer via primary
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task AddingVia_ClearsTheAmbiguity() {
        // One variant per flipped shape (the five fixtures S-11 turned into errors):
        // declaring `via` on one side clears DMREL005 and the rest of the domain.
        var variants = new (string Name, string Dsl)[] {
            ("fixture-1 create-in backrefs", """
                domain VariantCreateIn
                Customer: entity {
                  orders: many Order via customer
                }
                Order: entity {
                  Title: Text
                  customer: Customer via orders
                  primaryContact: Customer
                }
                """),
            ("fixture-2 self-relationship", """
                domain VariantSelfRel
                Account: entity {
                  Name: Text
                  primary: Account
                  secondary: Account
                  children: many Account via secondary
                }
                """),
            ("fixture-3 public create", """
                domain VariantPublicCreate
                Peer: entity {
                  primary: many Child
                  secondary: many Child
                }
                Child: entity {
                  peer: Peer via primary
                }
                """),
            ("fixture-4 create-in primary", """
                domain VariantCreateInPrimary
                Peer: entity {
                  primary: many Child
                  secondary: many Child
                  Make: action {
                    create in primary {}
                  }
                }
                Child: entity {
                  peer: Peer via primary
                }
                """),
            ("fixture-5 simulate type-create", """
                domain VariantSimulate
                Patron: entity {
                  Name: Text
                  fines: many Fine
                  waived: many Fine
                }
                Fine: entity {
                  Amount: Number
                  patron: Patron via fines
                }
                """),
        };

        foreach (var (name, dsl) in variants) {
            var analysis = Analyze(dsl);
            await Assert.That(AllErrors(analysis)).IsEmpty()
                .Because($"variant '{name}' should be clean once 'via' is declared");
        }
    }

    [Test]
    public async Task DeclaredSelfPair_IsClean() {
        var analysis = Analyze("""
            domain DeclaredSelfPair
            Account: entity {
              Name: Text
              mother: Account via father
              father: Account via mother
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    [Test]
    public async Task SelfNamingCollectionInverse_Error() {
        // Rule 3 in collection form: the navigation names itself as its inverse.
        var analysis = Analyze("""
            domain SelfNamingCollection
            Account: entity {
              Name: Text
              friends: many Account via friends
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        var nav = (Poly.DomainModeling.Ontology.Relationship)errors[0].Node;
        await Assert.That(nav.Name).IsEqualTo("friends");
    }

    [Test]
    public async Task DuplicatedTarget_ReportsOnlyStructural() {
        // Rule 1: a duplicated target type is DMSTR001's; the inverse rules skip rather
        // than cascade on whichever declaration won.
        var analysis = Analyze("""
            domain DuplicatedTarget
            A: entity {
              x: B via y
            }
            B: entity {
              Name: Text
            }
            B: entity {
              y: A
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate).Count)
            .IsGreaterThan(0);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseUnknown)).IsEmpty();
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory)).IsEmpty();
    }

    [Test]
    public async Task AbsentTarget_IsRejectedBeforeAnalysis() {
        // Rule 1's other half: a navigation to an undeclared type never reaches analysis —
        // the resolver rejects it at parse, so no inverse rule can cascade.
        var ex = Assert.Throws<Poly.Grammar.GrammarException>(() => new PolyDslParser("""
            domain AbsentTarget
            A: entity {
              x: Ghost via y
            }
            """).Parse());
        await Assert.That(ex!.Message).Contains("Ghost");
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

    [Test]
    public async Task OwnedWithCollectionInverse_IsDMREL004() {
        // S-12: an owned target has one owner, so its paired inverse must be singular.
        var analysis = Analyze("""
            domain OwnedCollectionInverse
            Order: entity {
              Total: Number
              lines: many owned Line via orders
            }
            Line: entity {
              sku: Text
              orders: many Order via lines
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(((Poly.DomainModeling.Ontology.Relationship)errors[0].Node).Name).IsEqualTo("lines");
    }

    [Test]
    public async Task OwnedWithCollectionInverse_OnlyThePlainSideDeclares_IsDMREL004() {
        // Reverse-declared orientation: only Line.orders names the pairing.
        var analysis = Analyze("""
            domain OwnedCollectionInverseReverse
            Order: entity {
              Total: Number
              lines: many owned Line
            }
            Line: entity {
              sku: Text
              orders: many Order via lines
            }
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(AllErrors(analysis).Count).IsEqualTo(1);
        await Assert.That(((Poly.DomainModeling.Ontology.Relationship)errors[0].Node).Name).IsEqualTo("orders");
    }

    /// <summary>Pin: green before the rule — an owned edge with a singular inverse is the legal pairing.</summary>
    [Test]
    public async Task OwnedWithSingularInverse_IsClean_Pin() {
        var analysis = Analyze("""
            domain OwnedSingularInverse
            Order: entity {
              Total: Number
              lines: many owned Line via order
            }
            Line: entity {
              sku: Text
              order: Order via lines
            }
            """);

        await Assert.That(AllErrors(analysis)).IsEmpty();
    }

    /// <summary>Pin: green before the rule — without `via` the two navigations are independent (Q10).</summary>
    [Test]
    public async Task OwnedWithCollectionInverse_Undeclared_IsClean_Pin() {
        var analysis = Analyze("""
            domain OwnedCollectionUndeclared
            Order: entity {
              Total: Number
              lines: many owned Line
            }
            Line: entity {
              sku: Text
              orders: many Order
            }
            """);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InverseContradictory)).IsEmpty();
    }
}