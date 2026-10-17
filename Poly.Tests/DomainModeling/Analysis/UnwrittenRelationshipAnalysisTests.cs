using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T3: a subscription or <c>for</c> loop that reads a collection relationship no
/// <c>create in</c> writes is a published plan that can never fire and a loop that matches
/// nothing — the case where analysis used to answer "nothing breaks".
/// </summary>
public class UnwrittenRelationshipAnalysisTests {
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        var domain = result.Root!;
        return (domain, DomainModelAnalyzer.Analyze(domain));
    }

    private static IReadOnlyList<Diagnostic> DeadEdges(AnalysisResult analysis) =>
        analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.UnwrittenRelationship)
            .ToList();

    [Test]
    public async Task SecondCollectionToTheSameChild_IsReported() {
        // Two owners declare a collection to Claim; the aggregate picks one (Policyholder,
        // declared first) and the generated child-create endpoint writes that edge. The
        // other collection has no writer at all, so `for claims …` matched zero targets.
        var (_, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              claims: many Claim
              ReviewAll: action {
                for claims as claim where claim in Filed
                  invoke claim.StartReview
              }
            }
            Claim: entity {
              Number: Text
              holder: Policyholder
              cover: Policy
              Filed: stage {
                StartReview: action { transition to UnderReview }
              }
              UnderReview: stage { }
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Policy.claims'");
        await Assert.That(dead[0].Message).Contains("'for' loop");
    }

    [Test]
    public async Task PeerCollectionReadByASubscription_IsReported() {
        // Session owns the create-in, so Session.bookings is written — but Member declares
        // its own collection to the same child and subscribes to it. Bookings reserved
        // through Session never reach Member, so its subscription never fires.
        var (_, analysis) = Analyze("""
            domain Test
            Member: entity {
              Name: Text
              Visits: Number default(0)
              bookings: many Booking
              when bookings Attended {
                assign Visits to Visits + 1
              }
            }
            Session: entity {
              Name: Text
              bookings: many Booking
              Reserve: action (who: Member) -> Booking {
                create in bookings { member: who }
              }
            }
            Booking: entity {
              member: Member
              session: Session
              Booked: stage { }
              Attended: stage { }
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Member.bookings'");
        await Assert.That(dead[0].Message).Contains("'Attended'");
    }

    [Test]
    public async Task CreatedInCollection_IsNotReported() {
        var (_, analysis) = Analyze("""
            domain Test
            Member: entity {
              Name: Text
              Visits: Number default(0)
              bookings: many Booking
              Reserve: action -> Booking {
                create in bookings { }
              }
              when bookings Attended {
                assign Visits to Visits + 1
              }
            }
            Booking: entity {
              member: Member
              Booked: stage { }
              Attended: stage { }
            }
            """);

        await Assert.That(DeadEdges(analysis)).IsEmpty();
    }

    [Test]
    public async Task AggregateParentRelationship_IsNotReported() {
        // The aggregate parent relationship is written by the generated child-create
        // endpoint and the printed Attach call, so a subscription on it is honest even
        // though no `create in` exists.
        var (_, analysis) = Analyze("""
            domain Test
            Order: entity {
              Reference: Text
              lines: many Line
              when lines Picked {
                assign Reference to Reference
              }
            }
            Line: entity {
              Sku: Text
              order: Order
              Waiting: stage { }
              Picked: stage { }
            }
            """);

        await Assert.That(DeadEdges(analysis)).IsEmpty();
    }

    [Test]
    public async Task SingularRelationship_IsNotReported() {
        // A singular edge can be satisfied by a value set at create, so "no create in" does
        // not make it dead.
        var (_, analysis) = Analyze("""
            domain Test
            Plan: entity {
              Code: Text
              Active: stage { }
            }
            Member: entity {
              Name: Text
              Visits: Number default(0)
              plan: Plan
              when plan Active {
                assign Visits to Visits + 1
              }
            }
            """);

        await Assert.That(DeadEdges(analysis)).IsEmpty();
    }

    [Test]
    public async Task CountExpression_OnUnwrittenCollection_IsReported() {
        // Policy.claims is the second collection to the same child; the aggregate picked
        // Policyholder.claims, so `count claims` counts an edge nothing writes.
        var (_, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              claims: many Claim
              Big: policy { count claims > 5 }
            }
            Claim: entity {
              Number: Text
              holder: Policyholder
              cover: Policy
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Policy.claims'");
        await Assert.That(dead[0].Message).Contains("'count'");
    }

    [Test]
    public async Task AnyExpression_OnUnwrittenCollection_IsReported() {
        var (_, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              claims: many Claim
              Open: policy { any claims where Label is "C1" }
            }
            Claim: entity {
              Label: Text
              holder: Policyholder
              cover: Policy
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Policy.claims'");
        await Assert.That(dead[0].Message).Contains("'any'");
    }

    [Test]
    public async Task RelationshipExists_OnUnwrittenCollection_IsReported() {
        var (_, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              claims: many Claim
              HasClaims: policy { claims exists }
            }
            Claim: entity {
              Number: Text
              holder: Policyholder
              cover: Policy
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Policy.claims'");
        await Assert.That(dead[0].Message).Contains("'exists'");
    }

    [Test]
    public async Task SumExpression_OnUnwrittenCollection_IsReported() {
        var (_, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              claims: many Claim
              Total: policy { sum claims Amount > 0 }
            }
            Claim: entity {
              Amount: Number
              holder: Policyholder
              cover: Policy
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(dead[0].Message).Contains("'Policy.claims'");
        await Assert.That(dead[0].Message).Contains("'sum'");
    }

    [Test]
    public async Task QuantifierExpressions_OnWrittenCollection_AreNotReported() {
        // `create in bookings` writes Member.bookings, so count / any / exists over it are honest.
        var (_, analysis) = Analyze("""
            domain Test
            Member: entity {
              Name: Text
              bookings: many Booking
              Reserve: action -> Booking {
                create in bookings { }
              }
              Big: policy { count bookings > 5 }
              Open: policy { any bookings where Label is "B1" }
              HasBookings: policy { bookings exists }
            }
            Booking: entity {
              member: Member
              Label: Text
            }
            """);

        await Assert.That(DeadEdges(analysis)).IsEmpty();
    }

    [Test]
    public async Task SingularRelationshipExists_IsNotReported() {
        // The boundary from the base slice: a singular edge can be populated by an
        // explicit link or a value set at create, so `plan exists` is not judged.
        var (_, analysis) = Analyze("""
            domain Test
            Plan: entity {
              Code: Text
              Active: stage { }
            }
            Member: entity {
              Name: Text
              plan: Plan
              HasPlan: policy { plan exists }
            }
            """);

        await Assert.That(DeadEdges(analysis)).IsEmpty();
    }

    [Test]
    public async Task ExpressionReader_ReportsOnTheExpressionElementPath() {
        // The read is anchored on the counted expression inside the action, so the finding's
        // element path resolves to the action (the nearest named ancestor), not the domain.
        var (domain, analysis) = Analyze("""
            domain Test
            Policyholder: entity {
              Name: Text
              claims: many Claim
            }
            Policy: entity {
              Number: Text
              Count: Number default(0)
              claims: many Claim
              Tally: action { assign Count to count claims }
            }
            Claim: entity {
              Number: Text
              holder: Policyholder
              cover: Policy
            }
            """);

        var dead = DeadEdges(analysis);
        await Assert.That(dead.Count).IsEqualTo(1);
        await Assert.That(DomainElementPath.Resolve(domain, dead[0].Node)).IsEqualTo("Test/Policy/Tally");
    }
}