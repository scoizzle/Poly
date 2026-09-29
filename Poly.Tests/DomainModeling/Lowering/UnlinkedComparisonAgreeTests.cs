using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class UnlinkedComparisonAgreeTests {
    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
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

    private const string AdvisorDsl = """
        domain Shop
        Advisor: entity {
          Age: Number
        }
        Customer: entity {
          Age: Number
          Ready: Boolean default(false)
          advisor: Advisor
          YoungerThanAdvisor: policy { Age < advisor Age }
          Promote: action require YoungerThanAdvisor {
            assign Ready to true
          }
        }
        """;

    [Test]
    public async Task UnlinkedAdvisorComparisons_AreFalse_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = Evolve("""
            domain Shop
            Advisor: entity {
              Name: Text
              Age: Number
            }
            Customer: entity {
              Age: Number
              advisor: Advisor
              YoungerThanAdvisor: policy { Age < advisor Age }
              AdvisorUnderThirty: policy { advisor Age < 30 }
              NamedPat: policy { advisor Name is "Pat" }
              NotPat: policy { advisor Name is not "Pat" }
            }
            """);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var customerE = E("Customer");
        var store = new DomainInstanceStore();
        var customer = DomainEntityInstance.Create(customerE,
            new Dictionary<string, object?> { ["Age"] = 20L }, domain);
        store.Add(customer);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L));

        foreach (var policyName in new[] {
            "YoungerThanAdvisor", "AdvisorUnderThirty", "NamedPat", "NotPat"
        }) {
            var sim = customer.EvaluatePolicy(customerE.Policies.First(p => p.Name == policyName));
            var print = (bool)printed.GetType().GetMethod(policyName)!.Invoke(printed, null)!;
            await Assert.That(sim).IsFalse().Because($"simulate {policyName}");
            await Assert.That(print).IsEqualTo(sim).Because($"print {policyName}");
        }
    }

    [Test]
    public async Task UnlinkedAdvisorGuard_BlocksAction_OnSimulateAndPrintedCsharp() {
        var (domain, analysis) = Evolve(AdvisorDsl);
        Entity E(string name) => domain.Types.OfType<Entity>().First(e => e.Name == name);
        var store = new DomainInstanceStore();
        var customer = DomainEntityInstance.Create(E("Customer"),
            new Dictionary<string, object?> { ["Age"] = 20L, ["Ready"] = false }, domain);
        store.Add(customer);

        var sim = customer.InvokeAction("Promote");
        await Assert.That(sim.Succeeded).IsFalse();
        await Assert.That(customer.GetProperty<bool>("Ready")).IsFalse();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var printed = ExportedCSharp.CreateEntity(asm, "Customer", ("age", 20L), ("ready", false));
        var result = printed.GetType().GetMethod("Promote")!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!).IsFalse();
        await Assert.That((bool)printed.GetType().GetProperty("Ready")!.GetValue(printed)!).IsFalse();
    }
}
