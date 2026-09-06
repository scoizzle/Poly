using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation;

/// <summary>Positive <see cref="ResolvedJumpTarget"/> stamps and remaining JT diagnostics.</summary>
public class JumpTargetAnalysisTests {
    private static AnalysisResult AnalyzeJumps(Node node) =>
        new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseJumpTargetResolution()
            .Build()
            .Analyze(node);

    [Test]
    public async Task Break_InsideWhile_StampsResolvedJumpTarget() {
        var br = new BreakStatement();
        var loop = new WhileLoop(new Constant(true), br);
        var result = AnalyzeJumps(loop);
        var target = result.GetMetadata<ResolvedJumpTarget>(br);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.TargetNodeId).IsEqualTo(loop.Id);
        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task Continue_InsideFor_StampsResolvedJumpTarget() {
        var cont = new ContinueStatement();
        var i = new Variable("i");
        var loop = new ForLoop(
            new Assignment(i, new Constant(0L)),
            new LessThan(i, new Constant(3L)),
            new Assignment(i, new Add(i, new Constant(1L))),
            cont);
        var root = new Block([loop], [i]);
        var result = AnalyzeJumps(root);
        var target = result.GetMetadata<ResolvedJumpTarget>(cont);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.TargetNodeId).IsEqualTo(loop.Id);
    }

    [Test]
    public async Task Goto_KnownLabel_StampsResolvedJumpTarget() {
        var label = new LabelDeclaration("exit", new Constant(1L));
        var g = new GotoStatement("exit");
        var result = AnalyzeJumps(new Block([g, label]));
        var target = result.GetMetadata<ResolvedJumpTarget>(g);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.TargetNodeId).IsEqualTo(label.Id);
    }

    [Test]
    public async Task LabeledContinue_UnknownLabel_ReportsJT0003() {
        var node = new WhileLoop(new Constant(true), new ContinueStatement("nope"));
        var result = AnalyzeJumps(node);
        await Assert.That(result.Diagnostics.Any(d => d.Code == "JT0003")).IsTrue();
        await Assert.That(() => Interpreter.Compile(node)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LabeledBreak_MatchingOuter_StampsOuterLoop() {
        var br = new BreakStatement("outer");
        var inner = new WhileLoop(new Constant(true), br);
        var outer = new WhileLoop(new Constant(true), inner, Label: "outer");
        var result = AnalyzeJumps(outer);
        var target = result.GetMetadata<ResolvedJumpTarget>(br);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.TargetNodeId).IsEqualTo(outer.Id);
    }
}
