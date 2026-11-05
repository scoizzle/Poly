using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// Slice B oracle: Lower at Lower — execute never lowers.
/// Interpreter runs cached module / EntryExit / policy bodies only.
/// <para>
/// The segment-cache cases were deleted with the cache itself (#388): contiguous non-transition
/// fragments of a mixed OnEntry list had no production reader, and the feature they described —
/// execute binding segments — was never implemented.
/// </para>
/// </summary>
public class SliceBLowerAtLowerTests {
    [Test]
    public async Task SliceB_EvaluatePolicy_UsesCachedBody_ClearThrowsFailClosed() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Permit: entity {
              Age: Number
              Adult: policy { Age >= 18 }
            }
            """);
        var exec = ModuleExec.For(domain);
        _ = session.Lower(domain, analysis);

        var permitE = domain.Types.OfType<Entity>().First(e => e.Name == "Permit");
        var adult = exec.Create((permitE).Name, new Dictionary<string, object?> { ["Age"] = 25L }).Instance!;
        var policy = permitE.Policies.First(p => p.Name == "Adult");
        await Assert.That(exec.EvaluatePolicy(adult, (policy).Name)).IsTrue();

        RuntimeAnalysisCache.ClearPolicyBody(domain, "Permit", "Adult");
        var ex = Assert.Throws<InvalidOperationException>(() => exec.EvaluatePolicy(adult, (policy).Name));
        await Assert.That(ex!.Message).Contains("missing");
    }

    [Test]
    public async Task SliceB_NamedAction_StillRunsFromCachedModule() {
        var (domain, analysis, session) = Evolve("""
            domain Parking
            Lot: entity {
              Tag: Text
              Mark: action {
                assign Tag to "ok"
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var module = session.Lower(domain, analysis);
        var lotType = module.First(t => t.Name == "Lot");
        var mark = lotType.Methods?.FirstOrDefault(m => m.Name == "Mark");
        await Assert.That(mark?.Body).IsNotNull();

        var lotE = domain.Types.OfType<Entity>().First(e => e.Name == "Lot");
        var lot = exec.Create((lotE).Name, new Dictionary<string, object?> { ["Tag"] = "seed" }).Instance!;
        var result = exec.Invoke(lot, "Mark");
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((string)lot.Bag["Tag"]!).IsEqualTo("ok");
        await Assert.That(RuntimeAnalysisCache.TryGetModuleMethod(domain, "Lot", "Mark", out var cached)).IsTrue();
        await Assert.That(ReferenceEquals(mark, cached)).IsTrue();
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, ExtensionCatalog.ProductAuthoring);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }
}