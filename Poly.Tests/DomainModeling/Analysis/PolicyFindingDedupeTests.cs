using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Surface;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #483 / V-11: a <c>require P</c> copy of an entity policy shares <c>P</c>'s expression
/// <em>object</em> (the resolver appends <c>new Policy(name, expr)</c> holding the same
/// <see cref="DomainExpression"/>), so every tree-walking pass sees one node by two paths and
/// reports the same finding twice. One finding per distinct (node, code, message) per pass, and
/// per analysis run: the model is still rejected with the same severity, position and message.
/// </summary>
public class PolicyFindingDedupeTests {
    private const string SelfRelationship = "Quantifier on self-relationship";

    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    /// <summary>Findings of one code whose message carries <paramref name="message"/>.</summary>
    private static List<Poly.Analysis.Diagnostic> Findings(EvolutionResult r, string code, string message) =>
        r.Analysis.Diagnostics
            .Where(d => d.Code == code && d.Message.Contains(message, StringComparison.Ordinal))
            .ToList();

    private static List<Poly.Analysis.Diagnostic> Findings(
        Poly.Analysis.AnalysisResult analysis, string code, string message) =>
        analysis.Diagnostics
            .Where(d => d.Code == code && d.Message.Contains(message, StringComparison.Ordinal))
            .ToList();

    /// <summary>The verbatim #483 shape: a self-relationship read by an entity policy a action requires.</summary>
    private static string SelfReferential(string requires) => $$"""
        domain Org

        Employee: entity {
          Name: Text
          reports: many Employee
          NoReports: policy { none reports where Name == "x" }
          {{requires}}
        }
        """;

    // ── R1 / R2: the #483 repro, one finding per code ─────────────

    [Test]
    public async Task RequireReferencedPolicy_ReportsDMSEM005Once() {
        var r = Parse(SelfReferential("Fire: action require NoReports { }"));

        var errors = Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Severity).IsEqualTo(Poly.Analysis.DiagnosticSeverity.Error);
    }

    [Test]
    public async Task RequireReferencedPolicy_ReportsDMREL002Once() {
        var r = Parse(SelfReferential("Fire: action require NoReports { }"));

        var warnings = Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'");
        await Assert.That(warnings.Count).IsEqualTo(1);
        await Assert.That(warnings[0].Severity).IsEqualTo(Poly.Analysis.DiagnosticSeverity.Warning);
    }

    // ── R3 / R4 / R5: the shapes that wrap or branch the shared object ──

    [Test]
    public async Task RequireNotPolicy_ReportsEachCodeOnce() {
        var r = Parse(SelfReferential("Fire: action require not NoReports { }"));

        await Assert.That(Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
        await Assert.That(Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'").Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task StageRequirePolicy_ReportsEachCodeOnce() {
        var r = Parse("""
            domain Org

            Employee: entity {
              Name: Text
              reports: many Employee
              NoReports: policy { none reports where Name == "x" }
              Open: stage require NoReports { }
            }
            """);

        await Assert.That(Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
        await Assert.That(Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'").Count)
            .IsEqualTo(1);
    }

    [Test]
    public async Task TwoActionsRequiringTheSamePolicy_ReportEachCodeOnce() {
        var r = Parse(SelfReferential("""
            Fire: action require NoReports { }
            File: action require NoReports { }
            """));

        await Assert.That(Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
        await Assert.That(Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'").Count)
            .IsEqualTo(1);
    }

    // ── R6 / R7: guards on the gate's reach ──────────────────────

    [Test]
    public async Task PolicyAlone_ReportsEachCodeOnce() {
        var r = Parse("""
            domain Org

            Employee: entity {
              Name: Text
              reports: many Employee
              NoReports: policy { none reports where Name == "x" }
            }
            """);

        await Assert.That(Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
        await Assert.That(Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'").Count)
            .IsEqualTo(1);
    }

    /// <summary>Two policies with identical text are two identities: neither finding is dropped.</summary>
    [Test]
    public async Task TwoIdenticalPolicies_ReportEachCodeTwice() {
        var r = Parse("""
            domain Org

            Employee: entity {
              Name: Text
              reports: many Employee
              A: policy { none reports where Name == "x" }
              B: policy { none reports where Name == "x" }
            }
            """);

        await Assert.That(Findings(r, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(2);
        await Assert.That(Findings(r, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Employee.reports'").Count)
            .IsEqualTo(2);
    }

    // ── R8: the gate is per run, not per pass instance ───────────

    /// <summary>
    /// The warning shape only: a non-self collection no action writes, so analysis succeeds and
    /// the domain survives the gate. Two analyses of one domain must each report the finding once,
    /// which a pass field would break.
    /// </summary>
    [Test]
    public async Task SecondAnalysisOfOneDomain_ReportsDMREL002Once() {
        var root = Parse("""
            domain Shop

            Cart: entity {
              Name: Text
              bookings: many Booking
              OnlyBooked: policy { none bookings where Name == "x" }
              Check: action require OnlyBooked { }
            }

            Booking: entity {
              Name: Text
            }
            """);
        await Assert.That(root.Succeeded).IsTrue();

        var first = DomainModelAnalyzer.Analyze(root.Root);
        var second = DomainModelAnalyzer.Analyze(root.Root);

        await Assert.That(Findings(first, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Cart.bookings'").Count)
            .IsEqualTo(1);
        await Assert.That(Findings(second, DomainModelDiagnosticCodes.UnwrittenRelationship, "'Cart.bookings'").Count)
            .IsEqualTo(1);
    }

    /// <summary>
    /// The error shape, twice through one explicit shared session: the same pass instances run
    /// again, so a set held on the pass would report nothing the second time.
    /// </summary>
    [Test]
    public async Task SecondAnalysisOfOneDomain_ReportsDMSEM005Once() {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        foreach (var id in ExtensionCatalog.ProductAuthoring)
            builder.Load(ExtensionCatalog.Core.Resolve(id));
        var session = builder.Build();

        var changes = new PolyDslParser(SelfReferential("Fire: action require NoReports { }"), session).Parse();
        var evolution = new DomainEvolution(DomainTestFactory.Create("_", [], []));

        var first = evolution.ApplyAndAnalyze(changes, session: session);
        var second = evolution.ApplyAndAnalyze(changes, session: session);

        await Assert.That(Findings(first.Analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
        await Assert.That(Findings(second.Analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution, SelfRelationship).Count)
            .IsEqualTo(1);
    }

    // ── R9: distinct policies are both reported ──────────────────

    [Test]
    public async Task SharedAndDistinctPolicies_ReportBothFindings() {
        var r = Parse("""
            domain Org

            Employee: entity {
              Name: Text
              reports: many Employee
              NoReports: policy { none reports where Name == "x" }
              Other: policy { Missing == 1 }
              Fire: action require NoReports { }
            }
            """);

        var errors = r.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(2);
        await Assert.That(errors.Count(d => d.Message.Contains(SelfRelationship, StringComparison.Ordinal)))
            .IsEqualTo(1);
        await Assert.That(errors.Count(d => d.Message.Contains("Policy references property 'Missing'", StringComparison.Ordinal)))
            .IsEqualTo(1);
    }

    // ── R10: the issue's own door ───────────────────────────────

    /// <summary>The #483 repro through <c>poly_define</c>: one finding per code, and still rejected.</summary>
    [Test]
    public async Task Define_483Repro_ReportsOneFindingPerCode() {
        var surface = new PolySurfaceTools(McpTestTools.Store, NullLogger<PolySurfaceTools>.Instance);
        var workspace = surface.Workspace(name: "PolicyDedupe").State!.Workspace;

        var response = surface.Define(workspace, poly: SelfReferential("Fire: action require NoReports { }"));

        await Assert.That(response.Ok).IsFalse();
        var errors = response.Findings
            .Where(f => f.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution
                && f.Message.Contains(SelfRelationship, StringComparison.Ordinal))
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        var warnings = response.Findings
            .Where(f => f.Code == DomainModelDiagnosticCodes.UnwrittenRelationship
                && f.Message.Contains("'Employee.reports'", StringComparison.Ordinal))
            .ToList();
        await Assert.That(warnings.Count).IsEqualTo(1);
    }

    // ── R11 / R13: DMSEM003 (ExpressionTypeAnalyzer), scope-aware ─

    /// <summary>
    /// A policy the type pass rejects is walked twice, once as the entity policy and once as the
    /// action's require copy, so the mismatch is reported twice.
    /// </summary>
    [Test]
    public async Task RequireReferencedPolicy_ReportsDMSEM003Once() {
        var r = Parse("""
            domain Shop

            Order: entity {
              Name: Text
              P: policy { Name > 5 }
              Fire: action require P { }
            }
            """);

        var errors = r.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The same shared policy, required by an action whose parameter supplies the name the policy
    /// reads: the entity walk infers nothing (the name is no property there) while the action walk
    /// types it Text, so the action's finding exists only in that scope. A gate keyed on the
    /// expression alone would drop it; the gate keys on the parameter scope too.
    /// </summary>
    [Test]
    public async Task ShadowingActionParameter_KeepsContextDifferingFinding() {
        var r = Parse("""
            domain Shop

            Order: entity {
              Name: Text
              P: policy { Qty > 5 }
              Fire(Qty: Text): action require P { }
            }
            """);

        var errors = r.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
    }

    // ── R12: DMFUN001 (FunctionAnalysis via ExpressionTypeAnalyzer) ──

    [Test]
    public async Task RequireReferencedPolicy_ReportsDMFUN001Once() {
        var r = Parse("""
            domain Shop

            Order: entity {
              Name: Text
              P: policy { Name is Nope(1) }
              Fire: action require P { }
            }
            """);

        var errors = r.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.FunctionUnknown
                && d.Message.Contains("Nope", StringComparison.Ordinal))
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
    }
}