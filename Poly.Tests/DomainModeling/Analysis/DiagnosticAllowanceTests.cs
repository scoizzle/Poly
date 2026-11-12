using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

public class DiagnosticAllowanceTests {
    private const string NotesDomain = """
        domain Orders
        Customer: entity {
          Notes: Text length(0, 200)
        }
        Order: entity {
          Notes: Text length(0, 50)
          Qty: Number range(0, 10)
        }
        """;

    private const string EqualWidthDomain = """
        domain Orders
        Customer: entity {
          Notes: Text length(0, 50)
        }
        Order: entity {
          Notes: Text length(0, 50)
        }
        """;

    private const string NotesPath = "Orders/Order/Notes";

    private static readonly string[] Ids = ["plsql-1", "csharp-2", "perl-3"];

    private static ObservedFlow NotesFlow() => new("Customer.Notes", "Order.Notes");

    private static DiagnosticAllowance Allow(string code, string path, params string[] ids) =>
        new(code, path, ids.Length == 0 ? Ids : ids);

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
    public async Task ObservedFlow_UnboundedSource_IsNotYetVerified() {
        // PIN (review H1): a source with no same-kind constraint is not verified today. V-04 routes
        // observed flows through WriteRecorder and flips this; until then the flow is neither a
        // finding nor a fact. The counts are read null-safely so this pin does not depend on H3.
        var domain = """
            domain Orders
            Customer: entity {
              Amount: Number
              Active: stage { Next: action { transition to Done } }
              Done: stage { }
            }
            Order: entity {
              Amount: Number range(0, 10)
              Open: stage { Close: action { transition to Closed } }
              Closed: stage { }
            }
            """;

        var (_, model, analysis) = Run(domain, [new ObservedFlow("Customer.Amount", "Order.Amount")]);

        // Zero diagnostics from the flow. (The domain carries one unrelated DMAS001 authoring
        // suggestion on Order, so the total is not asserted; the write codes and the flow are.)
        await Assert.That(Of(analysis, "DMEFF008").Count()).IsEqualTo(0);
        await Assert.That(Of(analysis, "DMCS003").Count()).IsEqualTo(0);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Observed flow", StringComparison.Ordinal))).IsFalse();
        var facts = analysis.GetMetadata<WriteVerificationFacts>(model);
        await Assert.That(facts?.Unverified.Count ?? 0).IsEqualTo(0);
        await Assert.That(facts?.Violations.Count ?? 0).IsEqualTo(0);
        await Assert.That(facts?.Verified ?? 0).IsEqualTo(0);
    }

    [Test]
    public async Task WidthNarrowing_NoAllowance_IsDMEFF008Error_AndLowerRefuses() {
        var (session, domain, analysis) = Run(NotesDomain, [NotesFlow()]);

        var finding = Of(analysis, "DMEFF008").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(DomainElementPath.Resolve(domain, finding.Node)).IsEqualTo(NotesPath);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);

        var lower = Assert.Throws<InvalidOperationException>(() => session.Lower(domain, analysis));
        await Assert.That(lower.Message).IsEqualTo(finding.Message);
        await Assert.That(lower.Message).DoesNotContain("open contradiction");
        var emit = Assert.Throws<InvalidOperationException>(() => session.Emit(domain, analysis));
        await Assert.That(emit.Message).IsEqualTo(finding.Message);
    }

    [Test]
    public async Task WidthNarrowing_MatchingAllowance_IsWarning_AndLowerSucceeds() {
        var (session, domain, analysis) = Run(
            NotesDomain, [NotesFlow()], [Allow("DMEFF008", NotesPath)]);

        await Assert.That(analysis.HasErrors).IsFalse();
        var finding = Of(analysis, "DMEFF008").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(DomainElementPath.Resolve(domain, finding.Node)).IsEqualTo(NotesPath);
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);

        await Assert.That(session.Lower(domain, analysis).Count).IsGreaterThan(0);
        var report = session.ArtifactCatalog.Artifacts
            .Select(a => a.Payload).OfType<AnalysisReport>()
            .SelectMany(r => r.Findings)
            .Where(f => f.Code == "DMEFF008").ToList();
        await Assert.That(report.Count).IsEqualTo(1);
        await Assert.That(report[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(report[0].ElementPath).IsEqualTo(NotesPath);
    }

    [Test]
    public async Task Allowance_WrongCode_OrStaleAfterEqualWidth_IsDMALW001() {
        // Wrong code: the DMEFF008 finding stays an error and the allowance is unused.
        var (wrongSession, wrongDomain, wrong) = Run(
            NotesDomain, [NotesFlow()], [Allow("DMSEM003", NotesPath)]);
        await Assert.That(Of(wrong, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(wrong, "DMALW001").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        Assert.Throws<InvalidOperationException>(() => wrongSession.Lower(wrongDomain, wrong));

        // Wrong path: same.
        var (_, _, otherPath) = Run(
            NotesDomain, [NotesFlow()], [Allow("DMEFF008", "Orders/Order/Qty")]);
        await Assert.That(Of(otherPath, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(Of(otherPath, "DMALW001").Count()).IsEqualTo(1);

        // Stale: the flow became compatible, the allowance is left over.
        var (staleSession, staleDomain, stale) = Run(
            EqualWidthDomain, [NotesFlow()], [Allow("DMEFF008", NotesPath)]);
        await Assert.That(Of(stale, "DMEFF008").Count()).IsEqualTo(0);
        await Assert.That(Of(stale, "DMALW001").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
        Assert.Throws<InvalidOperationException>(() => staleSession.Lower(staleDomain, stale));

        // No provenance is not an acknowledgement: it does not downgrade.
        foreach (var ids in new[] { Array.Empty<string>(), new[] { "" }, new[] { "ok", "  " } }) {
            var (_, _, anonymous) = Run(
                NotesDomain, [NotesFlow()], [new DiagnosticAllowance("DMEFF008", NotesPath, ids)]);
            await Assert.That(Of(anonymous, "DMEFF008").Single().Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(Of(anonymous, "DMALW001").Count()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task TypeMismatch_NoAllowance_IsDMSEM003_AllowanceDowngrades() {
        var flow = new ObservedFlow("Customer.Notes", "Order.Qty");
        const string qtyPath = "Orders/Order/Qty";

        var (_, domain, analysis) = Run(NotesDomain, [flow]);
        var finding = Of(analysis, "DMSEM003").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(DomainElementPath.Resolve(domain, finding.Node)).IsEqualTo(qtyPath);

        var (session, allowedDomain, allowed) = Run(NotesDomain, [flow], [Allow("DMSEM003", qtyPath)]);
        await Assert.That(allowed.HasErrors).IsFalse();
        await Assert.That(Of(allowed, "DMSEM003").Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(Of(allowed, "DMALW001").Count()).IsEqualTo(0);
        await Assert.That(session.Lower(allowedDomain, allowed).Count).IsGreaterThan(0);
    }

    [Test]
    public async Task UnresolvablePath_IsDMSEM005_AllowanceDoesNotClearIt() {
        foreach (var flow in new[] {
            new ObservedFlow("Customer.Missing", "Order.Notes"),
            new ObservedFlow("Customer.Notes", "Order"),
            new ObservedFlow("Orders.Customer.Notes", "Order.Notes"),
            new ObservedFlow("", "Order.Notes"),
            new ObservedFlow("Order.Notes", "Order.Notes"),
        }) {
            var (_, domain, bare) = Run(NotesDomain, [flow]);
            await Assert.That(Of(bare, "DMSEM005").Any(d => d.Severity == DiagnosticSeverity.Error)).IsTrue();

            var (session, allowedDomain, allowed) = Run(
                NotesDomain, [flow], [Allow("DMSEM005", "Orders"), Allow("DMEFF008", NotesPath)]);
            await Assert.That(Of(allowed, "DMSEM005").Any(d => d.Severity == DiagnosticSeverity.Error)).IsTrue();
            await Assert.That(Of(allowed, "DMALW001").Count()).IsEqualTo(2);
            Assert.Throws<InvalidOperationException>(() => session.Lower(allowedDomain, allowed));
            _ = domain;
        }
    }

    // Existing shape (ConstraintPropagationEffectTests): a parameter with a wider range assigned to
    // a narrower property. The DSL cannot constrain a parameter, so this is built directly.
    private static Domain ParameterAssignDomain() {
        var customer = new Entity("Customer", [
            new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 200d)])
        ], [], [], []);
        var set = new Poly.DomainModeling.Ontology.Action("Set", InvocationResult.Void,
            [new Property("amount", new DomainTypeReference("Number"), [new RangeConstraint(0d, 200d)])],
            [new AssignEffect(DomainExpression.Property("Qty"), DomainExpression.Parameter("amount"))],
            []);
        var order = new Entity("Order", [
            new Property("Qty", new DomainTypeReference("Number"), [new RangeConstraint(0d, 100d)])
        ], [set], [], []);
        return DomainTestFactory.Create(
            "Orders", [new PrimitiveType("Number", PrimitiveSemantics.Number, []), customer, order], []);
    }

    [Test]
    public async Task OrdinaryParameterAssign_StaysWarning() {
        var domain = ParameterAssignDomain();
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal().Build();
        var analysis = session.Analyze(domain);

        var finding = Of(analysis, "DMEFF008").Single();
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(finding.Message).Contains("allows values outside");
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(Of(analysis, "DMALW001").Count()).IsEqualTo(0);

        // A flow and its allowance elsewhere in the domain do not touch the ordinary assign.
        var flowDomain = ParameterAssignDomain();
        var flowSession = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows([new ObservedFlow("Customer.Qty", "Order.Qty")])
            .WithDiagnosticAllowances([Allow("DMEFF008", "Orders/Order/Qty")])
            .Build();
        var withFlow = flowSession.Analyze(flowDomain);
        await Assert.That(withFlow.HasErrors).IsFalse();
        var all = Of(withFlow, "DMEFF008").ToList();
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all.All(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();
        await Assert.That(all.Count(d => d.Message.StartsWith("Observed flow"))).IsEqualTo(1);

        // Without the allowance only the flow finding becomes an error; the ordinary one stays.
        var bareSession = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows([new ObservedFlow("Customer.Qty", "Order.Qty")]).Build();
        var bare = Of(bareSession.Analyze(ParameterAssignDomain()), "DMEFF008").ToList();
        await Assert.That(bare.Single(d => d.Message.StartsWith("Observed flow")).Severity)
            .IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(bare.Single(d => !d.Message.StartsWith("Observed flow")).Severity)
            .IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task WithDomain_CarriesFlowsAndAllowances() {
        var flows = new[] { NotesFlow() };
        var allowances = new[] { Allow("DMEFF008", NotesPath) };
        var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
            .WithObservedFlows(flows).WithDiagnosticAllowances(allowances).Build();
        await Assert.That(session.ObservedFlows.Count).IsEqualTo(1);
        await Assert.That(session.DiagnosticAllowances.Count).IsEqualTo(1);

        // Same extensions: tables are kept.
        var same = session.WithDomain(new Domain("Orders", []));
        await Assert.That(same.ObservedFlows.Single()).IsEqualTo(flows[0]);
        await Assert.That(same.DiagnosticAllowances.Single()).IsEqualTo(allowances[0]);

        // Extensions changed: the session reloads and still carries both.
        var reloaded = session.WithDomain(new Domain("Orders", []) { Extensions = ["storage"] });
        await Assert.That(ReferenceEquals(reloaded, session)).IsFalse();
        await Assert.That(reloaded.Extensions).IsEquivalentTo(new[] { "storage" });
        await Assert.That(reloaded.ObservedFlows.Single()).IsEqualTo(flows[0]);
        await Assert.That(reloaded.DiagnosticAllowances.Single()).IsEqualTo(allowances[0]);
    }

    [Test]
    public async Task Bind_DuplicateOrNullElement_Throws() {
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        await Assert.That(() => builder.WithObservedFlows([NotesFlow(), NotesFlow()]))
            .Throws<ArgumentException>();
        await Assert.That(() => builder.WithObservedFlows([null!])).Throws<ArgumentException>();
        await Assert.That(() => builder.WithDiagnosticAllowances([null!])).Throws<ArgumentException>();
    }

    [Test]
    public async Task ProductCatalog_DoesNotResolveContradictions() {
        await Assert.That(ProductCatalog.Catalog.Contains("contradictions")).IsFalse();
        await Assert.That(ProductCatalog.Model.Contains("contradictions")).IsFalse();
    }
}