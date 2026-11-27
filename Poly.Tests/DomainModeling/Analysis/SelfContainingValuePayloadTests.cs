using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// F3 (#1120): a value type used as an action payload (an action parameter, or a contract
/// payload bound to one) that contains itself — directly or through other value types — is
/// refused at analysis with a positioned diagnostic. Lower's payload guard walk is recursive,
/// so it used to throw <c>NotSupportedException</c> after a clean analyze; analysis now fails
/// closed first and the non-cyclic nested shapes still lower.
/// </summary>
public sealed class SelfContainingValuePayloadTests {
    private const string DirectCycle = """
        domain CyclicPayload

        Link: value {
          Label: Text
          Next: Link
        }

        Chain: entity {
          Holds: Boolean optional
          Walk: action (payload: Link) {
            assign Holds to true
          }
        }
        """;

    private const string IndirectCycle = """
        domain IndirectPayload

        A: value {
          B: B
        }

        B: value {
          A: A
        }

        Holder: entity {
          Flag: Boolean optional
          Run: action (a: A) {
            assign Flag to true
          }
        }
        """;

    private const string ContractPayloadCycle = """
        domain ContractPayload

        Billing: contract internal billing v1 {
          ChargeRequest: value {
            Amount: Number
            Next: ChargeRequest
          }
          Charge: outbound operation ChargeRequest
        }

        public entity Order {
          Total: Number default(0)
          Paid: Boolean optional
          public action Pay (request: ChargeRequest) {
            assign Paid to true
          }
        }

        ChargeOrder: bind Billing Charge to Pay request
        """;

    private const string NestedButAcyclic = """
        domain NestedPayload

        Inner: value {
          Amount: Decimal range(0, 10)
        }

        Outer: value {
          Label: Text
          Inner: Inner
        }

        Holder: entity {
          Flag: Boolean optional
          Run: action (o: Outer) {
            assign Flag to true
          }
        }
        """;

    [Test]
    public async Task DirectCycle_AsActionPayload_IsPositionedErrorNamingTheCycle() {
        var result = GuideBlocks.Analyze(DirectCycle);

        var error = result.Diagnostics.Single(d =>
            d.Code == DomainModelDiagnosticCodes.SelfContainingValuePayload);
        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(10);
        await Assert.That(error.Message)
            .Contains("payload")
            .And.Contains("Link")
            .And.Contains("'Link' -> 'Link'")
            .And.Contains("contains itself")
            .And.Contains("Break the cycle");
    }

    [Test]
    public async Task IndirectCycle_AsActionPayload_IsPositionedErrorNamingTheCycle() {
        var result = GuideBlocks.Analyze(IndirectCycle);

        var error = result.Diagnostics.Single(d =>
            d.Code == DomainModelDiagnosticCodes.SelfContainingValuePayload);
        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(13);
        await Assert.That(error.Message)
            .Contains("'A' -> 'B' -> 'A'")
            .And.Contains("Break the cycle");
    }

    [Test]
    public async Task ContractPayloadCycle_IsPositionedErrorNamingTheCycle() {
        var result = GuideBlocks.Analyze(ContractPayloadCycle);

        var error = result.Diagnostics.Single(d =>
            d.Code == DomainModelDiagnosticCodes.SelfContainingValuePayload);
        await Assert.That(error.Placed).IsTrue();
        await Assert.That(error.Line).IsEqualTo(14);
        await Assert.That(error.Message)
            .Contains("'ChargeRequest' -> 'ChargeRequest'")
            .And.Contains("Break the cycle");
    }

    [Test]
    public async Task AcyclicNestedValuePayload_AnalyzesCleanAndLowers() {
        var analysis = Analyze(NestedButAcyclic);

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SelfContainingValuePayload)).IsFalse();
        await Assert.That(() => Lower(NestedButAcyclic)).ThrowsNothing();
    }

    private static AnalysisResult Analyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(
            new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static IReadOnlyList<Node> Lower(string dsl) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return session.Lower(result.Root!, result.Analysis);
    }
}