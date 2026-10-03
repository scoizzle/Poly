using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.TestHelpers;

/// <summary>Shared DSL-to-domain helper for simulate-vs-print agreement tests.</summary>
public static class EvolvedDomain {
    /// <summary>Parses and analyzes <paramref name="poly"/>, throwing on any error diagnostic.</summary>
    public static (Domain Domain, AnalysisResult Analysis) FromDsl(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics
                    .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }
}
