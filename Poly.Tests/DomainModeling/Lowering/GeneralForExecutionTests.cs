using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #409 slice 6: the general <c>for x in Rel [where cond] { effects }</c> lowers to a real
/// loop over the relationship's records and executes: body once per matching record,
/// fail-fast on the first failure, zero records a no-op.
/// </summary>
public class GeneralForExecutionTests {
    private const string Dsl = """
        domain Billing
        Line: entity {
          Qty: Number
          Marks: Number default(0)
          Open: stage {
            Mark: action { assign Marks to Marks + 1 }
          }
        }
        Invoice: entity {
          Note: Text default("none")
          lines: many Line
          Open: stage {
            Touch: action {
              for line in lines where line Qty > 1 {
                assign Note to "touched"
                invoke line.Mark
              }
            }
          }
        }
        """;

    private const string FailFastDsl = """
        domain Billing
        Line: entity {
          Qty: Number
          Marks: Number default(0)
          HasQty: policy { Qty > 0 }
          Open: stage {
            Mark: action require HasQty { assign Marks to Marks + 1 }
          }
        }
        Invoice: entity {
          lines: many Line
          Open: stage {
            Touch: action {
              for line in lines {
                invoke line.Mark
              }
            }
          }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) Evolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
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

    private static long Marks(ModuleInstance line) => Convert.ToInt64(line.Bag["Marks"]);

    [Test]
    public async Task GeneralFor_Executes_BodyPerMatchingRecord() {
        var (domain, _) = Evolve(Dsl);
        var exec = ModuleExec.For(domain);
        var small = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        var big1 = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 5L }).Instance!;
        var big2 = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 9L }).Instance!;
        var invoice = exec.Create("Invoice", new Dictionary<string, object?>()).Instance!;
        exec.Link("lines", invoice, small);
        exec.Link("lines", invoice, big1);
        exec.Link("lines", invoice, big2);

        var result = exec.Invoke(invoice, "Touch");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Marks(small)).IsEqualTo(0L);
        await Assert.That(Marks(big1)).IsEqualTo(1L);
        await Assert.That(Marks(big2)).IsEqualTo(1L);
        await Assert.That((string)invoice.Bag["Note"]!).IsEqualTo("touched");
    }

    [Test]
    public async Task GeneralFor_ZeroRecords_IsNoOp_AndActionSucceeds() {
        var (domain, _) = Evolve(Dsl);
        var exec = ModuleExec.For(domain);
        var invoice = exec.Create("Invoice", new Dictionary<string, object?>()).Instance!;

        var emptyResult = exec.Invoke(invoice, "Touch");

        await Assert.That(emptyResult.Succeeded).IsTrue();
        await Assert.That((string)invoice.Bag["Note"]!).IsEqualTo("none");

        var small = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        exec.Link("lines", invoice, small);
        var noMatchResult = exec.Invoke(invoice, "Touch");

        await Assert.That(noMatchResult.Succeeded).IsTrue();
        await Assert.That((string)invoice.Bag["Note"]!).IsEqualTo("none");
        await Assert.That(Marks(small)).IsEqualTo(0L);
    }

    [Test]
    public async Task GeneralFor_FirstFailure_FailsWholeFor_AndStopsInvokingLaterRecords() {
        var (domain, analysis) = Evolve(FailFastDsl);
        var exec = ModuleExec.For(domain);
        var ok1 = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        var bad = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 0L }).Instance!;
        var later = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        var invoice = exec.Create("Invoice", new Dictionary<string, object?>()).Instance!;
        exec.Link("lines", invoice, ok1);
        exec.Link("lines", invoice, bad);
        exec.Link("lines", invoice, later);

        var result = exec.Invoke(invoice, "Touch");

        // The simulator rolls back the whole failed action, so no record keeps a mark.
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Marks(ok1)).IsEqualTo(0L);
        await Assert.That(Marks(bad)).IsEqualTo(0L);
        await Assert.That(Marks(later)).IsEqualTo(0L);

        // Generated C# has no rollback, so it shows where the loop stopped: the record before
        // the failure ran, the record after it was never invoked.
        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var lineType = asm.GetType("Line")!;
        var invoiceType = asm.GetType("Invoice")!;
        var lines = Array.CreateInstance(lineType, 3);
        lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("qty", 1L)), 0);
        lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("qty", 0L)), 1);
        lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("qty", 1L)), 2);
        var printed = ExportedCSharp.CreateEntity(invoiceType, ("lines", lines));
        var printedResult = invoiceType.GetMethod("Touch", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)printedResult.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(printedResult)!).IsFalse();
        var after = ((System.Collections.IEnumerable)invoiceType.GetProperty("Lines", PrintedMembers.Any)!.GetValue(printed)!)
            .Cast<object>().Select(l => Convert.ToInt64(lineType.GetProperty("Marks", PrintedMembers.Any)!.GetValue(l))).ToList();
        await Assert.That(after).IsEquivalentTo(new List<long> { 1L, 0L, 0L });
    }

    [Test]
    public async Task GeneralFor_SimulateAndGeneratedCSharp_Agree() {
        var (domain, analysis) = Evolve(Dsl);
        var exec = ModuleExec.For(domain);
        var small = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;
        var big = exec.Create("Line", new Dictionary<string, object?> { ["Qty"] = 5L }).Instance!;
        var invoice = exec.Create("Invoice", new Dictionary<string, object?>()).Instance!;
        exec.Link("lines", invoice, small);
        exec.Link("lines", invoice, big);
        await Assert.That(exec.Invoke(invoice, "Touch").Succeeded).IsTrue();

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var lineType = asm.GetType("Line")!;
        var invoiceType = asm.GetType("Invoice")!;
        var lines = Array.CreateInstance(lineType, 2);
        lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("qty", 1L)), 0);
        lines.SetValue(ExportedCSharp.CreateEntity(lineType, ("qty", 5L)), 1);
        var printed = ExportedCSharp.CreateEntity(invoiceType, ("lines", lines));
        var result = invoiceType.GetMethod("Touch", PrintedMembers.Any)!.Invoke(printed, null)!;
        await Assert.That((bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!).IsTrue();

        var after = ((System.Collections.IEnumerable)invoiceType.GetProperty("Lines", PrintedMembers.Any)!.GetValue(printed)!)
            .Cast<object>().ToList();
        long PrintedMarks(object l) => Convert.ToInt64(lineType.GetProperty("Marks", PrintedMembers.Any)!.GetValue(l));
        await Assert.That(PrintedMarks(after[0])).IsEqualTo(Marks(small));
        await Assert.That(PrintedMarks(after[1])).IsEqualTo(Marks(big));
        await Assert.That(invoiceType.GetProperty("Note", PrintedMembers.Any)!.GetValue(printed)).IsEqualTo(invoice.Bag["Note"]);
    }

    [Test]
    public async Task GeneralFor_CrossEntityWrite_StillRejected_DMEFF012() {
        var changes = new PolyDslParser(Dsl).Parse();
        var domain = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes).Root;
        var planted = new AssignEffect(
            new RelationshipNavigation("line", DomainExpression.Property("Qty")),
            DomainExpression.Literal(0L));
        var types = domain.Types.Select(t => t is Entity { Name: "Invoice" } invoice
            ? invoice with {
                Stages = invoice.Stages.Select(s => s with {
                    Actions = s.Actions.Select(a => a with {
                        Effects = a.Effects.Select(e => e is ForEachEffect fe ? fe with { Body = [planted] } : e).ToList()
                    }).ToList()
                }).ToList()
            }
            : t).ToList();

        var errors = DomainModelAnalyzer.Analyze(domain with { Types = types }).Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).ToList();

        await Assert.That(errors.Any(d => d.Code == DomainModelDiagnosticCodes.EffectCrossEntityMutation)).IsTrue();
        await Assert.That(errors.Any(d => d.Code == DomainModelDiagnosticCodes.EffectForNotLowerable)).IsFalse();
    }
}