using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class ExceptionRegionAnalysisTests {
    private static AnalysisResult Analyze(Node node) {
        return new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseConstantFolding()
            .UseDefiniteAssignmentAnalysis()
            .UseLambdaReturnTypeResolution()
            .UseExceptionRegionAnalysis()
            .Build()
            .Analyze(node);
    }

    [Test]
    public async Task NoExceptionHandling_NoRegions() {
        var node = new Block(new Constant(1), new Constant(2));
        var result = Analyze(node);

        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryFinally_ProducesTryAndFinallyRegions() {
        var tryBody = new Block(new Constant(1));
        var finallyBody = new Block(new Constant(2));
        var node = new TryCatchFinally(tryBody, CatchClauses: null, FinallyBlock: finallyBody);

        var result = Analyze(node);

        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();

        var regionList = regions!.ToList();
        // The pass generates regions for each TryCatchFinally node
        // At minimum: Try region + Finally region
        await Assert.That(regionList.Count).IsGreaterThanOrEqualTo(2);

        // First region should be Try
        await Assert.That(regionList[0].Kind).IsEqualTo(ExceptionRegionKind.Try);
        await Assert.That(regionList[0].AnchorNodeId).IsEqualTo(node.Id);

        // Last region should be Finally
        await Assert.That(regionList[^1].Kind).IsEqualTo(ExceptionRegionKind.Finally);
        await Assert.That(regionList[^1].AnchorNodeId).IsEqualTo(node.Id);
    }

    [Test]
    public async Task TryCatch_ProducesTryAndCatchRegions() {
        var tryBody = new Block(new Constant(1));
        var catchBody = new Block(new Constant(2));
        var catchClause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchBody);
        var node = new TryCatchFinally(tryBody, CatchClauses: [catchClause]);

        var result = Analyze(node);

        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();

        var regionList = regions!.ToList();
        await Assert.That(regionList.Count).IsGreaterThanOrEqualTo(2);

        // First is Try
        await Assert.That(regionList[0].Kind).IsEqualTo(ExceptionRegionKind.Try);
        // Second is Catch
        await Assert.That(regionList[1].Kind).IsEqualTo(ExceptionRegionKind.Catch);
        await Assert.That(regionList[1].CatchVariableName).IsEqualTo("ex");
    }

    [Test]
    public async Task NoRegionsForPlainBlock_ReturnsEmpty() {
        var node = new Block(Wrap(1), Wrap(2));
        var result = Analyze(node);

        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ThrowOutsideProtectedRegion_NotMarked() {
        // A throw outside try/catch is not in a protected region
        var node = new ThrowStatement(new Constant("error"));
        var result = Analyze(node);

        var inProtected = result.IsInProtectedRegion(node as ThrowStatement ?? throw new Exception());
        await Assert.That(inProtected).IsFalse();
    }

    [Test]
    public async Task ThrowInsideTry_IsMarkedProtected() {
        // A throw inside a try block should be marked as in a protected region.
        // Use a direct throw (no Block wrapper) to keep subtree collection simple.
        var throwStmt = new ThrowStatement(new Constant("error"));
        var catchBody = new Block(new Constant(99));
        var catchClause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchBody);
        var node = new TryCatchFinally(throwStmt, CatchClauses: [catchClause]);

        var result = Analyze(node);

        var inProtected = result.IsInProtectedRegion(throwStmt);
        await Assert.That(inProtected).IsTrue();

        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(regions[0].Kind).IsEqualTo(ExceptionRegionKind.Try);
        await Assert.That(regions[1].Kind).IsEqualTo(ExceptionRegionKind.Catch);
        await Assert.That(regions[1].CatchTypeName).IsEqualTo("System.Exception");
    }

    [Test]
    public async Task TryCatchFinally_AllRegionsPresent() {
        var catchBody = new Block(new Constant(1));
        var finallyBody = new Block(new Constant(2));
        var clause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchBody);
        var node = new TryCatchFinally(new Block(new Constant(42)), CatchClauses: [clause], FinallyBlock: finallyBody);

        var result = Analyze(node);
        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsGreaterThanOrEqualTo(3);

        // Order: Try, Catch, Finally
        await Assert.That(regions[0].Kind).IsEqualTo(ExceptionRegionKind.Try);
        await Assert.That(regions[1].Kind).IsEqualTo(ExceptionRegionKind.Catch);
        await Assert.That(regions[2].Kind).IsEqualTo(ExceptionRegionKind.Finally);
    }

    [Test]
    public async Task SameAnalyzer_TwoSequentialAnalyses_NoRegionLeak() {
        // ANA-FIX-014: State isolation across sequential analyze calls.
        var analyzer = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseControlFlowAnalysis()
            .UseConstantFolding()
            .UseDefiniteAssignmentAnalysis()
            .UseLambdaReturnTypeResolution()
            .UseExceptionRegionAnalysis()
            .Build();

        // Analyze tree A with try/catch
        var throwStmt = new ThrowStatement(new Constant("error"));
        var tryBody = new Block(throwStmt);
        var catchBody = new Block(new Constant(99));
        var clause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchBody);
        var treeA = new TryCatchFinally(tryBody, CatchClauses: [clause]);
        var resultA = analyzer.Analyze(treeA);
        var regionsA = resultA.GetExceptionRegions();
        await Assert.That(regionsA).IsNotNull();
        await Assert.That(regionsA!.Count).IsGreaterThan(0);

        // Analyze tree B with no EH — regions must be empty
        var treeB = new Block(new Constant(1), new Constant(2));
        var resultB = analyzer.Analyze(treeB);
        var regionsB = resultB.GetExceptionRegions();
        await Assert.That(regionsB).IsNotNull();
        await Assert.That(regionsB!.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ThrowInCatch_NotInProtectedRegion() {
        // A throw inside a catch clause is NOT in the protected (try) region.
        // The catch body is the handler — throws inside it are unprotected.
        var catchThrow = new ThrowStatement(new Constant("catch error"));
        var tryBody = new Block(new Constant(42));
        var catchClause = new CatchClause(
            ExceptionType: new TypeReference("System.Exception"),
            VariableName: "ex",
            Body: catchThrow);
        var node = new TryCatchFinally(tryBody, CatchClauses: [catchClause]);

        var result = Analyze(node);

        // The throw inside the catch body should NOT be marked as protected
        var inProtected = result.IsInProtectedRegion(catchThrow);
        await Assert.That(inProtected).IsFalse();
    }

    [Test]
    public async Task NestedTry_InnerThrowMarkedProtected() {
        // Throw inside inner try → IsInProtectedRegion == true.
        // Throws in catch clauses are not protected — see ThrowInCatch_NotInProtectedRegion.
        var innerThrow = new ThrowStatement(new Constant("inner error"));
        var innerTry = new TryCatchFinally(
            innerThrow,
            CatchClauses: [new CatchClause(
                ExceptionType: new TypeReference("System.Exception"),
                VariableName: "ex",
                Body: new Block(new Constant(1)))]);

        var outerTry = new TryCatchFinally(
            innerTry,
            CatchClauses: [new CatchClause(
                ExceptionType: new TypeReference("System.Exception"),
                VariableName: "ex2",
                Body: new Block(new Constant(2)))]);

        var result = Analyze(outerTry);

        // Inner throw is inside a protected region (inner try)
        var inProtected = result.IsInProtectedRegion(innerThrow);
        await Assert.That(inProtected).IsTrue();

        // Should have at least 4 regions: outer try, outer catch, inner try, inner catch
        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task UsingStatement_ProducesUsingDisposeRegion() {
        var resource = new Constant("resource");
        var body = new Block(new Constant(42));
        var node = new UsingStatement(resource, body);

        var result = Analyze(node);
        var regions = result.GetExceptionRegions();
        await Assert.That(regions).IsNotNull();
        await Assert.That(regions!.Count).IsGreaterThanOrEqualTo(1);

        var usingRegion = regions.FirstOrDefault(r => r.Kind == ExceptionRegionKind.UsingDispose);
        await Assert.That(usingRegion).IsNotNull();
        await Assert.That(usingRegion!.AnchorNodeId).IsEqualTo(node.Id);

        // Protected set should include the body and resource
        await Assert.That(usingRegion.ProtectedNodeIds.Count).IsGreaterThan(0);
        await Assert.That(usingRegion.ProtectedNodeIds).Contains(body.Id);
    }

    private static Node Wrap(int value) => new Constant(value);
}