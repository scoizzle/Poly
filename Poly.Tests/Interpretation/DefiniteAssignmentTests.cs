using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>Asserts <see cref="DefiniteAssignmentMetadata"/> stamped by the DA pass (not only pipeline inclusion).</summary>
public class DefiniteAssignmentTests {
    private static AnalysisResult AnalyzeDa(Node node) =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);

    [Test]
    public async Task LambdaBody_AfterAssignment_IsDefinitelyAssigned() {
        var x = new Variable("x");
        var body = new Block([
            new Assignment(x, new Constant(1L)),
            x
        ], [x]);
        var lambda = new Lambda([], body);
        var result = AnalyzeDa(lambda);
        await Assert.That(result.IsDefinitelyAssigned(body, "x")).IsTrue();
    }

    [Test]
    public async Task LambdaBody_UnassignedLocal_IsNotDefinitelyAssigned() {
        var x = new Variable("x");
        var body = new Block([x], [x]);
        var lambda = new Lambda([], body);
        var result = AnalyzeDa(lambda);
        await Assert.That(result.IsDefinitelyAssigned(body, "x")).IsFalse();
    }

    [Test]
    public async Task IfBothBranchesAssign_MergesIntoDefinitelyAssigned() {
        var x = new Variable("x");
        var body = new Block([
            new IfStatement(
                new Constant(true),
                new Assignment(x, new Constant(1L)),
                new Assignment(x, new Constant(2L))),
            x
        ], [x]);
        var lambda = new Lambda([], body);
        var result = AnalyzeDa(lambda);
        await Assert.That(result.IsDefinitelyAssigned(body, "x")).IsTrue();
    }

    [Test]
    public async Task IfOnlyThenAssigns_DoesNotMergeAsDefinitelyAssigned() {
        var x = new Variable("x");
        var body = new Block([
            new IfStatement(
                new Constant(true),
                new Assignment(x, new Constant(1L))),
            x
        ], [x]);
        var lambda = new Lambda([], body);
        var result = AnalyzeDa(lambda);
        // Only then-branch assigns; join must not claim definite assignment.
        await Assert.That(result.IsDefinitelyAssigned(body, "x")).IsFalse();
    }
}
