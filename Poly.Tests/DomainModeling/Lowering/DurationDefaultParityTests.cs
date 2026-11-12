using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #468: <c>Budget: Duration default(30 Minutes)</c> must reach an instance created
/// without the argument on both sides — the interpreter module and the printed C# —
/// or lowering must fail closed. A silent drop (0, null, or a required parameter)
/// is the bug this row pins.
/// </summary>
public class DurationDefaultParityTests {
    private const string Dsl = """
        domain Sla
        uses temporal
        Job: entity {
          Budget: Duration default(30 Minutes)
        }
        """;

    private static readonly TimeSpan ThirtyMinutes = TimeSpan.FromMinutes(30);

    private static (Domain Domain, AnalysisResult Analysis) ParseTemporal(string dsl) {
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return (evolved.Root!, evolved.Analysis);
    }

    private static string PrintTemporal(string dsl) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(ExtensionCatalog.Core.Resolve(ExtensionCatalog.TemporalId));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    [Test]
    public async Task DefaultDuration_OmittedBudget_IsThirtyMinutesOnSimulateAndPrintedCsharp() {
        var (domain, _) = ParseTemporal(Dsl);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Job");

        var created = exec.Create(entity.Name, new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsTrue();
        var simulated = (ModuleInstance)created.Instance!;
        await Assert.That(simulated.Bag["Budget"]).IsEqualTo(ThirtyMinutes);

        var csharp = PrintTemporal(Dsl);
        var asm = ExportedCSharp.CompileAndLoad(csharp, "DurationDefaultParity");
        var result = ExportedCSharp.InvokeCreate(asm.GetType("Job")!);
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();
        var printed = result.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(result)!;
        await Assert.That(printed.GetType().GetProperty("Budget", PrintedMembers.Any)!.GetValue(printed)).IsEqualTo(ThirtyMinutes);
        await Assert.That(csharp).Contains("TimeSpan.FromMinutes(30L)");
    }
}