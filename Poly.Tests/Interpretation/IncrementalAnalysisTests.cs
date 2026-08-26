using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

public class IncrementalAnalysisTests {
    private static Analyzer CreateIncrementalAnalyzer() => new AnalyzerBuilder()
        .UseIncrementalAnalysis()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseValueRepresentationAnalysis()
        .UseCallSiteCatalog()
        .UseDefiniteAssignmentAnalysis()
        .UseExceptionRegionAnalysis()
        // .UsePrimitiveExpansion() — deprecated, non-critical path
        .Build();

    [Test]
    public async Task Incremental_SecondProgram_HasEmptyCatalogAndRegions() {
        var analyzer = CreateIncrementalAnalyzer();

        var invoke = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('e'));
        var tryCatch = new TryCatchFinally(
            new Block(new Constant(42)),
            CatchClauses: [new CatchClause(new TypeReference("System.Exception"), "ex", new Block(new Constant(1)))]);
        var treeA = new Block(invoke, tryCatch);
        analyzer.Analyze(treeA);

        var treeB = new Block(new Constant(1), new Constant(2));
        var resultB = analyzer.Analyze(treeB);

        var catalog = resultB.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsEqualTo(0);

        var regions = resultB.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Incremental_EditedInvoke_MatchesFullReanalysis() {
        var analyzer = CreateIncrementalAnalyzer();

        var invoke = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('e'));
        var root = new Block(invoke);
        var prior = analyzer.Analyze(root);

        var updatedInvoke = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('l'))
            with { Id = invoke.Id };
        var updatedRoot = new Block(updatedInvoke) with { Id = root.Id };

        var incremental = analyzer.Analyze(updatedRoot, prior, [updatedInvoke]);
        var full = analyzer.Analyze(updatedRoot);

        var incCatalog = incremental.GetCallSiteCatalog()!;
        var fullCatalog = full.GetCallSiteCatalog()!;
        await Assert.That(incCatalog.Count).IsEqualTo(fullCatalog.Count);
        for (int i = 0; i < fullCatalog.Count; i++) {
            await Assert.That(incCatalog[i].Identity).IsEqualTo(fullCatalog[i].Identity);
            await Assert.That(incCatalog[i].ArgCount).IsEqualTo(fullCatalog[i].ArgCount);
        }
    }

    [Test]
    public async Task Incremental_EditedTryCatch_MatchesFullReanalysis() {
        var analyzer = CreateIncrementalAnalyzer();

        var tryBody = new Block(new Constant(42));
        var tryCatch = new TryCatchFinally(
            tryBody,
            CatchClauses: [new CatchClause(new TypeReference("System.Exception"), "ex", new Block(new Constant(1)))]);
        var root = new Block(tryCatch);
        var prior = analyzer.Analyze(root);

        var updatedTryBody = new Block(new Constant(99)) with { Id = tryBody.Id };
        var updatedTryCatch = tryCatch with { TryBlock = updatedTryBody, Id = tryCatch.Id };
        var updatedRoot = new Block(updatedTryCatch) with { Id = root.Id };

        var incremental = analyzer.Analyze(updatedRoot, prior, [updatedTryBody]);
        var full = analyzer.Analyze(updatedRoot);

        var incRegions = incremental.GetExceptionRegions()!;
        var fullRegions = full.GetExceptionRegions()!;
        await Assert.That(incRegions.Count).IsEqualTo(fullRegions.Count);
        for (int i = 0; i < fullRegions.Count; i++) {
            await Assert.That(incRegions[i].Kind).IsEqualTo(fullRegions[i].Kind);
            await Assert.That(incRegions[i].AnchorNodeId).IsEqualTo(fullRegions[i].AnchorNodeId);
        }
    }
}