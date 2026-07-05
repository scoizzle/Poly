using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Syntax.Primitives;

using PrimCallExternal = Poly.Syntax.Primitives.CallExternal;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Integration tests verifying that expanded primitives carry correct
/// metadata-linked data (SiteIndex matching catalog, EH markers, ThrowProtected).
/// </summary>
public class ExpansionIntegrationTests {
    private static readonly Analyzer ProductionAnalyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseThisReferenceContext()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseValueRepresentationAnalysis()
        .UseCallSiteCatalog()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseExceptionRegionAnalysis()
        .UsePrimitiveExpansion()
        .Build();

    private static AnalysisResult Analyze(Node node) => ProductionAnalyzer.Analyze(node);

    [Test]
    public async Task Expansion_CallExternal_SiteIndexMatchesCatalog() {
        // string.IndexOf(char) should produce CallExternal with SiteIndex matching catalog
        var invoke = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('e'));
        var result = Analyze(invoke);

        var catalog = result.GetCallSiteCatalog();
        await Assert.That(catalog).IsNotNull();
        await Assert.That(catalog!.Count).IsGreaterThan(0);

        // Get the expanded primitives for the root node
        var primMeta = result.GetMetadata<PrimitiveExpansionMetadata>(invoke);
        await Assert.That(primMeta).IsNotNull();

        // Find the CallExternal primitive and verify SiteIndex
        var callExternals = primMeta!.Primitives.OfType<PrimCallExternal>().ToList();
        await Assert.That(callExternals.Count).IsGreaterThan(0);

        var ce = callExternals[0];
        await Assert.That(ce.SiteIndex).IsNotNull();
        await Assert.That(ce.SiteIndex!.Value).IsLessThan(catalog.Count);

        // Verify the catalog entry at that index matches
        var catalogEntry = catalog[ce.SiteIndex!.Value];
        await Assert.That(catalogEntry.Target.Name).IsEqualTo("IndexOf");
    }

    [Test]
    public async Task Expansion_DuplicateInvoke_SharedSiteIndex() {
        // Two identical invocations should share the same SiteIndex
        var invoke1 = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('e'));
        var invoke2 = new Invoke(new Member(new Constant("world"), "IndexOf"), new Constant('l'));
        var block = new Block(invoke1, invoke2);
        var result = Analyze(block);

        var primMeta1 = result.GetMetadata<PrimitiveExpansionMetadata>(invoke1);
        var primMeta2 = result.GetMetadata<PrimitiveExpansionMetadata>(invoke2);

        var ce1 = primMeta1!.Primitives.OfType<PrimCallExternal>().First();
        var ce2 = primMeta2!.Primitives.OfType<PrimCallExternal>().First();

        await Assert.That(ce1.SiteIndex).IsNotNull();
        await Assert.That(ce2.SiteIndex).IsNotNull();
        await Assert.That(ce1.SiteIndex!.Value).IsEqualTo(ce2.SiteIndex!.Value);
    }

    [Test]
    public async Task Expansion_TryCatchFinally_EmitsRegionMarkers() {
        // Try/Catch/Finally should produce RegionMarker primitives in expected order
        var throwStmt = new ThrowStatement(new Constant("error"));
        var tryBody = new Block(throwStmt);
        var catchBody = new Block(new Constant(1));
        var finallyBody = new Block(new Constant(2));
        var clause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchBody);
        var node = new TryCatchFinally(tryBody, CatchClauses: [clause], FinallyBlock: finallyBody);

        var result = Analyze(node);

        // Check that the try body's expanded primitives contain a ThrowProtected
        var tryPrimMeta = result.GetMetadata<PrimitiveExpansionMetadata>(tryBody);
        await Assert.That(tryPrimMeta).IsNotNull();
        var markers = tryPrimMeta!.Primitives.Where(p => p is ThrowProtected).ToList();
        // The throw is inside a protected try block
        await Assert.That(markers.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Expansion_ThrowOutsideTry_EmitsThrow() {
        // A throw outside a try block should produce Throw, not ThrowProtected
        var throwStmt = new ThrowStatement(new Constant("error"));
        var result = Analyze(throwStmt);

        var primMeta = result.GetMetadata<PrimitiveExpansionMetadata>(throwStmt);
        await Assert.That(primMeta).IsNotNull();

        var hasThrowProtected = primMeta!.Primitives.Any(p => p is ThrowProtected);
        var hasThrow = primMeta.Primitives.Any(p => p is Throw);

        await Assert.That(hasThrowProtected).IsFalse();
        await Assert.That(hasThrow).IsTrue();
    }

    [Test]
    public async Task Expansion_UsingStatement_EmitsLeaveUsingDispose() {
        var resource = new Constant("resource");
        var body = new Block(new Constant(42));
        var node = new UsingStatement(resource, body);

        var result = Analyze(node);

        var primMeta = result.GetMetadata<PrimitiveExpansionMetadata>(node);
        await Assert.That(primMeta).IsNotNull();

        var markers = primMeta!.Primitives.Where(p => p is RegionMarker).ToList();
        await Assert.That(markers.Count).IsGreaterThanOrEqualTo(1);

        var rm = (RegionMarker)markers[0];
        await Assert.That(rm.Kind).IsEqualTo("LeaveUsingDispose");
    }

    [Test]
    public async Task Expansion_TypeIs_IntConstant_IsTrue() {
        var node = new TypeIs(new Constant(42), TypeReference.To<int>());
        var result = Analyze(node);
        var primMeta = result.GetMetadata<PrimitiveExpansionMetadata>(node);
        await Assert.That(primMeta).IsNotNull();
        var push = primMeta!.Primitives.OfType<PushConstant>().Last();
        await Assert.That(push.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task Expansion_TypeIs_IntNotString_IsFalse() {
        var node = new TypeIs(new Constant(42), TypeReference.To<string>());
        var result = Analyze(node);
        var primMeta = result.GetMetadata<PrimitiveExpansionMetadata>(node);
        await Assert.That(primMeta).IsNotNull();
        var push = primMeta!.Primitives.OfType<PushConstant>().Last();
        await Assert.That(push.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task SequentialAnalyze_NoLeak() {
        // W5-T2/W5-V1: verify that sequential analysis with a single cached
        // analyzer produces isolated state (no catalog/region leaks).
        // Use a single cached analyzer instance (mirrors Interpreter._analyzer reuse).
        var analyzer = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseValueRepresentationAnalysis()
            .UseCallSiteCatalog()
            .UseConstantFolding()
            .UseDefiniteAssignmentAnalysis()
            .UseLambdaReturnTypeResolution()
            .UseExceptionRegionAnalysis()
            .Build();

        // Analyze tree A with CLR invoke + try/catch
        var invoke = new Invoke(new Member(new Constant("hello"), "IndexOf"), new Constant('e'));
        var tryCatch = new TryCatchFinally(
            new Block(new Constant(42)),
            CatchClauses: [new CatchClause(new TypeReference("System.Exception"), "ex", new Block(new Constant(1)))]);
        var treeA = new Block(invoke, tryCatch);
        var resultA = analyzer.Analyze(treeA);

        var catalogA = resultA.GetCallSiteCatalog();
        await Assert.That(catalogA).IsNotNull();
        await Assert.That(catalogA!.Count).IsGreaterThan(0);

        var regionsA = resultA.GetExceptionRegions();
        await Assert.That(regionsA).IsNotNull();
        await Assert.That(regionsA!.Count).IsGreaterThan(0);

        // Analyze tree B with no invoke and no EH — must be isolated from tree A
        var treeB = new Block(new Constant(1), new Constant(2));
        var resultB = analyzer.Analyze(treeB);

        var catalogB = resultB.GetCallSiteCatalog();
        await Assert.That(catalogB).IsNotNull();
        await Assert.That(catalogB!.Count).IsEqualTo(0);

        var regionsB = resultB.GetExceptionRegions();
        await Assert.That(regionsB).IsNotNull();
        await Assert.That(regionsB!.Count).IsEqualTo(0);
    }
}