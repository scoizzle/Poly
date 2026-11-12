using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.Probes;

/// <summary>Probe tests for the evidence backlog. Red is intended: each states the EXPECTED behaviour.</summary>
public class EvidenceBacklogProbes {
    private static IReadOnlyList<Diagnostic> Diagnose(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var all = result.Analysis.Diagnostics.ToList();
        if (result.Root is not null)
            all.AddRange(DomainModelAnalyzer.Analyze(result.Root).Diagnostics);
        return all;
    }

    private static string Dump(IEnumerable<Diagnostic> ds) =>
        string.Join(" | ", ds.Select(d => $"{d.Severity} {d.Code} {d.Message}"));

    [Test]
    public async Task Item08_MixedLongUlongRange_DoesNotThrow() {
        var domain = DomainTestFactory.Create("D", [
            new ValueType("V", [new Property("Value", new DomainTypeReference("Number"),
                [new RangeConstraint(0L, ulong.MaxValue)])], [])]);
        Exception? thrown = null;
        try { DomainModelAnalyzer.Analyze(domain); } catch (Exception e) { thrown = e; }
        await Assert.That(thrown?.ToString()).IsNull();
    }

    [Test]
    public async Task Item19_UnknownFunctionReturnType_ReportsPositionedErrorNamingMoney() {
        var all = Diagnose("domain P\nfunction F(): Money = 1\n");
        var errors = all.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That("all=[" + Dump(all) + "]" + (errors.Any(d => d.Message.Contains("Money")) ? "ok" : "NOERRORNAMINGMONEY"))
            .DoesNotContain("NOERRORNAMINGMONEY");
    }

    [Test]
    public async Task Item20_ValueDoubleUnderscoreStageName_RejectedOrCompiles() {
        const string dsl = "domain P\nX: entity {\n  value__: stage { }\n  Two: stage { }\n}\n";
        string cs;
        try {
            var (domain, analysis) = EvolvedDomain.FromDsl(dsl);
            cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));
        } catch (InvalidOperationException) {
            return; // analysis rejected it: expected
        }
        var thrown = Record.Exception(() => ExportedCSharp.CompileAndLoad(cs, "ValueUnderscore"));
        await Assert.That(thrown?.Message.Split('\n')[0]).IsNull();
    }

    /// <summary>Error text from parse or analysis, or "" when the document is accepted.</summary>
    private static string ErrorsOf(string dsl) {
        try {
            return Dump(Diagnose(dsl).Where(d => d.Severity == DiagnosticSeverity.Error));
        } catch (FormatException e) {
            return "PARSE " + e.Message;
        }
    }

    private const string EntryHead = "domain E\nLine: entity {\n  N: Text optional\n  Z: action { assign N to \"z\" }\n}\n";

    [Test]
    public async Task Item02a_EntryInvoke_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  Count: Number optional\n  A: stage {\n    entry { invoke Bump }\n    Bump: action { assign Count to 1 }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item02b_EntryCreate_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  Count: Number optional\n  A: stage {\n    entry { create Line { N: \"a\" } }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item02c_EntryForInvoke_IsRejected() {
        var errors = ErrorsOf(EntryHead + "X: entity {\n  lines: many Line\n  A: stage {\n    entry { for lines as l invoke l.Z }\n  }\n}\n");
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Item17_ManyToOneToKeywordNamedEntity_PrintsAndRereadsCleanly() {
        const string dsl = "domain M\nOrder: entity {\n  Code: Text\n}\nSchedule: entity {\n  Day: Text\n}\n";
        var parsed = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var added = new DomainEvolution(parsed.Root).ApplyAndAnalyze([
            new AddRelationshipChange("schedule", new DomainTypeReference("Order"),
                new DomainTypeReference("Schedule"), RelationshipCardinality.ManyToOne, [])]);
        var printed = new DomainDslPrinter().Print(added.Root);
        string outcome;
        try {
            var reread = new DomainEvolution(DomainTestFactory.Create("_", [], []))
                .ApplyAndAnalyze(new PolyDslParser(printed).Parse());
            outcome = reread.Succeeded ? "ok" : "ANALYSIS " + reread.FailureSummary;
        } catch (FormatException e) {
            outcome = "PARSE " + e.Message;
        }
        await Assert.That(outcome + " <<" + printed.Replace('\n', '~') + ">>").StartsWith("ok");
    }

    [Test]
    public async Task Item17_F9_UsesOrder_IsPreservedByLoad() {
        var session = DomainSession.Create(["persistence", "temporal"], SessionOptions.Bare(ExtensionCatalog.Core));
        await Assert.That(string.Join(",", session.Extensions)).StartsWith("persistence,temporal");
    }

    [Test]
    public async Task Item17_F8_OmittedUsesRedefine_ReproducesFreshModel() {
        const string withUses = "domain A\nuses http\nT: entity { N: Text }\n";
        const string without = "domain B\nU: entity { M: Text }\n";
        var h = PolySurfaceHarness.New();
        var fresh = h.Open();
        h.Define(fresh, without);
        var freshDsl = h.Export(fresh, "dsl").Content;
        var dirty = h.Open();
        var first = h.Define(dirty, withUses);
        var second = h.Define(dirty, without);
        var dirtyDsl = h.Export(dirty, "dsl").Content;
        await Assert.That($"first={first.Success} second={second.Success}\n{dirtyDsl}").IsEqualTo($"first={first.Success} second={second.Success}\n{freshDsl}");
    }
}

internal static class Record {
    public static Exception? Exception(System.Action a) {
        try { a(); return null; } catch (Exception e) { return e; }
    }
}
