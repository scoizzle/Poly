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
        var entity = domain.Types.OfType<Entity>().First(e => e.Name == "Widget");

        await Assert.That(() => DomainEntityInstance.Create(entity, domain: domain))
            .Throws<ConstraintFailureException>()
            .WithMessageContaining("must be <= 10");

        var asm = ExportedCSharp.CompileAndLoad(
            new CSharpGenerator().Generate(new DomainToCSharpExporter().Export(domain, analysis)));
        var thrown = await Assert.That(() => ExportedCSharp.CreateEntity(asm, "Widget"))
            .Throws<TargetInvocationException>();
        await Assert.That(thrown!.InnerException!.GetType().Name).IsEqualTo("ConstraintFailureException");
        await Assert.That(thrown.InnerException is InvalidOperationException).IsFalse();
        await Assert.That(thrown.InnerException!.Message).Contains("must be <= 10");
    }
}
