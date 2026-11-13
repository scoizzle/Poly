using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

public class OnEntryConstraintAgreeTests {
    [Test]
    public async Task OnEntryRangeViolation_FailsClosed_OnSimulateAndPrintedCsharp() {
        // Score is range(1, 10); the entry assigns from Bump, whose value is only
        // known at run time, so the violation is not a static-analysis error.
        var (domain, analysis) = EvolvedDomain.FromDsl("""
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Bump: Number default(100)
              Draft: stage {
                entry { assign Score to Bump }
              }
            }
            """);
        var exec = ModuleExec.For(domain);
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");

        // L-04a: Create RETURNS the entry's failure on both hosts (it threw ConstraintFailureException before).
        var created = exec.Create((entity).Name, new Dictionary<string, object?>());
        await Assert.That(created.Succeeded).IsFalse();
        await Assert.That(created.Error!).Contains("must be <= 10");
        await Assert.That(created.Failure!.Kind).IsEqualTo(Poly.Runtime.DomainFailureKind.Range);

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis)));
        var printed = ExportedCSharp.InvokeCreate(asm.GetType("Widget")!);
        var printedType = printed.GetType();
        await Assert.That((bool)printedType.GetProperty("IsSuccess")!.GetValue(printed)!).IsFalse();
        await Assert.That((string)printedType.GetProperty("ErrorMessage")!.GetValue(printed)!).Contains("must be <= 10");
    }
}