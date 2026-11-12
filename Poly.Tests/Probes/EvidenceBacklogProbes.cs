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
}

internal static class Record {
    public static Exception? Exception(System.Action a) {
        try { a(); return null; } catch (Exception e) { return e; }
    }
}
