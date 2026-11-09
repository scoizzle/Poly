using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #439: a reader-found copy from a property that may hold null (<c>optional</c>) into a required
/// property is a flow diagnostic. No allowance acknowledges it (#444): only a provable guard clears a
/// copy in the tree, and an observed flow has no guard site.
/// </summary>
public class ObservedFlowNullabilityTests {
    private const string NullabilityDomain = """
        domain Orders
        Customer: entity {
          Code: Text
          Note: Text optional length(0, 50)
        }
        Order: entity {
          Note: Text length(0, 50)
          Memo: Text optional
        }
        """;

    private const string WidthAndNullabilityDomain = """
        domain Orders
        Customer: entity {
          Note: Text optional length(0, 200)
        }
        Order: entity {
          Note: Text length(0, 50)
        }
        """;

    private const string OptionalTargetDomain = """
        domain Orders
        Customer: entity {
          Note: Text optional length(0, 200)
        }
        Order: entity {
          Note: Text optional length(0, 50)
        }
        """;

    private const string NotePath = "Orders/Order/Note";

    private static readonly string[] Ids = ["plsql-1", "csharp-2", "perl-3"];

    private static ObservedFlow NotesFlow() => new("Customer.Note", "Order.Note");

    private static DiagnosticAllowance Allow(string code, string path) => new(code, path, Ids);

    private static (DomainSession Session, Domain Domain, AnalysisResult Analysis) Run(
        string poly,
        IReadOnlyList<ObservedFlow>? flows = null,
        IReadOnlyList<DiagnosticAllowance>? allowances = null) {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows(flows)
            .WithDiagnosticAllowances(allowances)
            .Build();
        var changes = new PolyDslParser(poly, session).Parse();
        var domain = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session).Root;
        return (session, domain, session.Analyze(domain));
    }

    private static IEnumerable<Poly.Analysis.Diagnostic> Of(AnalysisResult analysis, string code) =>
        analysis.Diagnostics.Where(d => d.Code == code);

    [Test]
    public async Task OptionalSource_RequiredTarget_NoAllowance_IsDMEFF014Error_AndLowerRefuses() {
        var (session, domain, analysis) = Run(NullabilityDomain, [NotesFlow()]);

        var finding = Of(analysis, "DMEFF014").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(DomainElementPath.Resolve(domain, finding.Node)).IsEqualTo(NotePath);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);

        var lower = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(lower.Message).IsEqualTo(finding.Message);
        var emit = Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Assert.That(emit.Message).IsEqualTo(finding.Message);
    }

    [Test]
    public async Task OptionalSource_RequiredTarget_MatchingAllowance_StaysError_IsDMALW002_AndLowerRefuses() {
        var (session, domain, analysis) = Run(
            NullabilityDomain, [NotesFlow()], [Allow("DMEFF014", NotePath)]);

        var finding = Of(analysis, "DMEFF014").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(DomainElementPath.Resolve(domain, finding.Node)).IsEqualTo(NotePath);
        await Assert.That(Of(analysis, "DMALW002").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);

        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task RequiredSource_OrOptionalTarget_ReportsNothing() {
        foreach (var flow in new[] {
            new ObservedFlow("Customer.Code", "Order.Note"),
            new ObservedFlow("Customer.Note", "Order.Memo"),
            new ObservedFlow("Customer.Code", "Order.Memo"),
        }) {
            var (_, _, analysis) = Run(NullabilityDomain, [flow]);
            await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task WidthAndNullability_OnOneFlow_AreTwoCodes_OneAllowanceLeavesTheOther() {
        var flow = NotesFlow();

        var (_, _, bare) = Run(WidthAndNullabilityDomain, [flow]);
        await Assert.That(Of(bare, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(bare, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);

        var (widthSession, widthDomain, widthOnly) = Run(
            WidthAndNullabilityDomain, [flow], [Allow("DMEFF008", NotePath)]);
        await Assert.That(Of(widthOnly, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Of(widthOnly, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(widthOnly, "DMALW001").Count()).IsEqualTo(0);
        Assert.Throws<InvalidOperationException>(() => widthSession.Lower(widthDomain, widthOnly));

        var (session, domain, both) = Run(
            WidthAndNullabilityDomain, [flow], [Allow("DMEFF008", NotePath), Allow("DMEFF014", NotePath)]);
        await Assert.That(Of(both, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Of(both, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(both, "DMALW001").Count()).IsEqualTo(0);
        await Assert.That(Of(both, "DMALW002").Count()).IsEqualTo(1);
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, both));
    }

    [Test]
    public async Task NullabilityAllowance_StaleAfterTargetBecomesOptional_IsDMALW001() {
        var (session, domain, analysis) = Run(
            OptionalTargetDomain, [NotesFlow()], [Allow("DMEFF014", NotePath)]);

        await Assert.That(Of(analysis, "DMEFF014").Count()).IsEqualTo(0);
        var unused = Of(analysis, "DMALW001").Single();
        await Assert.That(unused.Severity).IsEqualTo(DiagnosticSeverity.Error);
        Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
    }

    [Test]
    public async Task OrdinaryNullableParameterAssign_ToRequiredProperty_IsDMEFF014() {
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal().Build();
        var analysis = session.Analyze(NullableParameterAssignDomain());

        await Assert.That(Of(analysis, "DMEFF014").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(analysis.HasErrors).IsTrue();
    }

    /// <summary>An optional parameter assigned into a required property: the ordinary assign path,
    /// not a flow. The DSL cannot mark a parameter optional, so the shape is built directly
    /// (#435's <c>ParameterAssignDomain</c> is the precedent).</summary>
    private static Domain NullableParameterAssignDomain() {
        var set = new Poly.DomainModeling.Ontology.Action("Set", InvocationResult.Void,
            [new Property("note", new DomainTypeReference("Text"), [new OptionalConstraint()])],
            [new AssignEffect(DomainExpression.Property("Note"), DomainExpression.Parameter("note"))],
            []);
        var order = new Entity("Order", [
            new Property("Note", new DomainTypeReference("Text"), [new LengthConstraint(0, 50)])
        ], [set], [], []);
        return DomainTestFactory.Create(
            "Orders", [new PrimitiveType("Text", PrimitiveSemantics.Text, []), order], []);
    }

    [Test]
    public async Task UnresolvablePath_StillDMSEM005_AndANullabilityAllowanceIsUnused() {
        var flow = new ObservedFlow("Customer.Missing", "Order.Note");

        var (_, _, bare) = Run(NullabilityDomain, [flow]);
        await Assert.That(Of(bare, "DMSEM005").Any(d => d.Severity == DiagnosticSeverity.Error)).IsTrue();

        var (_, _, allowed) = Run(NullabilityDomain, [flow], [Allow("DMEFF014", NotePath)]);
        await Assert.That(Of(allowed, "DMSEM005").Any(d => d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(Of(allowed, "DMALW001").Count()).IsEqualTo(1);
    }
}