using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// L-04a (#683), the entry forms that stay legal whatever S-18 decides: <c>assign</c> and <c>transition</c>. The guard
/// that an <c>assign</c> carries (lock amendment 1) is the soundness half of the move: the initial entry and every
/// subscription handler gain a result type, and <c>CopiesStrongerGuardClosedProperty</c> must keep keying on the absence
/// of an action, not on the absence of a result type. The <c>invoke</c>, <c>create</c> and <c>for … invoke</c> forms are
/// in <see cref="InitialEntryFailureTests"/>, the class S-18 may delete.
/// </summary>
public class InitialEntryAssignTests {
    // A is range(1, 10); B is range(1, 10) and entry-assigned too, so when `assign A to B` runs B still holds the CLR
    // default 0. A guard elided because "B is guard-closed" would let A = 0 through. The constructor kept the guard on
    // master because it had no result type.
    private const string ElisionDsl = """
        domain Elide
        Z: entity {
          A: Number range(1, 10) default(5)
          B: Number range(1, 10) default(5)
          Start: stage {
            entry {
              assign A to B
              assign B to 5
            }
          }
        }
        """;

    [Test]
    public async Task EntryAssign_WhenTheSourceStillHoldsItsDefault_KeepsTheTargetsGuardOnBothHosts() {
        var scenario = ParityScenario.FromDsl(ElisionDsl, "InitialEntryElision", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => side.Create("Z"));

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        foreach (var outcome in new[] { simulate[^1], printed[^1] }) {
            await Assert.That(outcome.Success).IsFalse();
            await Assert.That(outcome.Message!).Contains("'A' must be >= 1");
        }
    }

    // The same copy in a subscription handler: B is guard-closed there, so the guard is not observable by a value, but the
    // printed handler must still carry it. A handler is not an action: no `ActionName`, so nothing is elided (and no
    // golden hunk of an unexpected kind appears when the handler gains its result type).
    private const string HandlerElisionDsl = """
        domain ElideHandler
        Owner: entity {
          A: Number range(1, 10) default(5)
          B: Number range(1, 10) default(5)
          items: many Item
          when items Done { assign A to B }
        }
        Item: entity {
          owner: Owner via items
          Open: stage { Finish: action { transition to Done } }
          Done: stage { }
        }
        """;

    [Test]
    public async Task HandlerAssign_WhenItCopiesAGuardClosedProperty_KeepsTheTargetsGuardInThePrintedHandler() {
        var session = ParitySessions.Create(ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(HandlerElisionDsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        var cs = new Poly.Interpretation.CSharp.CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));

        var handler = ExportedCSharp.ExtractMethod(cs, "internal DomainResult When");

        await Assert.That(handler).Contains("must be >= 1");
        await Assert.That(handler).Contains("'A'");
    }

    [Test]
    public async Task Create_WhenTheEntryAssigns_SetsTheValueOnBothHosts() {
        var scenario = ParityScenario.FromDsl(InitialEntryDomains.Dsl("assign Score to 5"), "InitialEntryAssignOk", recordGraph: true);

        var (simulate, printed) = scenario.Run(side => side.Create("Kid"));

        await Assert.That(ParityScenario.Differences(simulate, printed)).IsEmpty();
        await Assert.That(simulate[^1].State["Score"]).IsEqualTo("5");
        await Assert.That(printed[^1].State["Score"]).IsEqualTo("5");
    }
}