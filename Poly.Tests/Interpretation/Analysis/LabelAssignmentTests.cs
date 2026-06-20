using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>Tests for <see cref="LabelAssignmentPass"/> metadata computation.</summary>
public sealed class LabelAssignmentTests {
    private static Analyzer AnalyzerWithLabels =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLabelAssignment()
            .Build();

    private static Analyzer AnalyzerWithoutLabels =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .Build();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static AnalysisResult Analyze(Node node) => AnalyzerWithLabels.Analyze(node);

    // ── WhileLoop ───────────────────────────────────────────────────────────

    [Test]
    public async Task WhileLoop_HasContAndEndLabels() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var result = Analyze(loop);
        var md = result.GetMetadata<WhileLoopLabelMetadata>(loop);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.ContLabel).IsNotEqualTo(md.EndLabel);
        await Assert.That(md.ContLabel).IsGreaterThanOrEqualTo(0);
        await Assert.That(md.EndLabel).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task WhileLoop_LabelsAreSequential() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var result = Analyze(loop);
        var md = result.GetMetadata<WhileLoopLabelMetadata>(loop)!;
        // Cont should be the first allocated label (n), End should be n+1.
        await Assert.That(md.EndLabel).IsEqualTo(md.ContLabel + 1);
    }

    [Test]
    public async Task TwoWhileLoops_HaveDistinctLabelRanges() {
        var loop1 = new WhileLoop(new Constant(1L), new Constant(0L));
        var loop2 = new WhileLoop(new Constant(1L), new Constant(0L));
        var block = new Block([loop1, loop2]);
        var result = Analyze(block);
        var md1 = result.GetMetadata<WhileLoopLabelMetadata>(loop1)!;
        var md2 = result.GetMetadata<WhileLoopLabelMetadata>(loop2)!;
        await Assert.That(md2.ContLabel).IsNotEqualTo(md1.ContLabel);
        await Assert.That(md2.EndLabel).IsNotEqualTo(md1.EndLabel);
        // Second loop's labels should come after the first loop's.
        await Assert.That(md2.ContLabel).IsGreaterThan(md1.EndLabel);
    }

    // ── Nested WhileLoops ───────────────────────────────────────────────────

    [Test]
    public async Task NestedWhileLoops_EachHaveOwnLabels() {
        var inner = new WhileLoop(new Constant(1L), new Constant(0L));
        var outer = new WhileLoop(new Constant(1L), inner);
        var result = Analyze(outer);
        var innerMd = result.GetMetadata<WhileLoopLabelMetadata>(inner)!;
        var outerMd = result.GetMetadata<WhileLoopLabelMetadata>(outer)!;
        await Assert.That(innerMd.ContLabel).IsNotEqualTo(outerMd.ContLabel);
        await Assert.That(innerMd.ContLabel).IsGreaterThan(outerMd.EndLabel);
    }

    // ── DoWhileLoop ─────────────────────────────────────────────────────────

    [Test]
    public async Task DoWhileLoop_HasContAndEndLabels() {
        var loop = new DoWhileLoop(new Constant(0L), new Constant(1L));
        var result = Analyze(loop);
        var md = result.GetMetadata<DoWhileLoopLabelMetadata>(loop);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.ContLabel).IsNotEqualTo(md.EndLabel);
        await Assert.That(md.EndLabel).IsEqualTo(md.ContLabel + 1);
    }

    // ── ForLoop ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ForLoop_HasCondAndEndLabels() {
        var v = new Variable("i");
        var loop = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            new Constant(0L));
        var result = Analyze(loop);
        var md = result.GetMetadata<ForLoopLabelMetadata>(loop);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.CondLabel).IsNotEqualTo(md.EndLabel);
        await Assert.That(md.EndLabel).IsEqualTo(md.CondLabel + 1);
    }

    // ── IfStatement ─────────────────────────────────────────────────────────

    [Test]
    public async Task IfWithElse_HasElseAndEndLabels() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var result = Analyze(iff);
        var md = result.GetMetadata<IfLabelMetadata>(iff);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.ElseLabel).IsNotNull();
        await Assert.That(md.ElseLabel!.Value).IsNotEqualTo(md.EndLabel);
        await Assert.That(md.EndLabel).IsGreaterThan(md.ElseLabel!.Value);
    }

    [Test]
    public async Task IfWithoutElse_HasNoElseLabel() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L));
        var result = Analyze(iff);
        var md = result.GetMetadata<IfLabelMetadata>(iff);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.ElseLabel).IsNull();
        await Assert.That(md.EndLabel).IsGreaterThanOrEqualTo(0);
    }

    // ── Conditional ─────────────────────────────────────────────────────────

    [Test]
    public async Task Conditional_HasFalseAndEndLabels() {
        var cond = new Conditional(new Constant(1L), new Constant(10L), new Constant(20L));
        var result = Analyze(cond);
        var md = result.GetMetadata<ConditionalLabelMetadata>(cond);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.FalseLabel).IsNotEqualTo(md.EndLabel);
        await Assert.That(md.EndLabel).IsEqualTo(md.FalseLabel + 1);
    }

    // ── Break / Continue ────────────────────────────────────────────────────

    [Test]
    public async Task BreakInsideWhile_HasEndTarget() {
        var breakStmt = new BreakStatement();
        var loop = new WhileLoop(new Constant(1L), breakStmt);
        var result = Analyze(loop);
        var breakMd = result.GetMetadata<BreakTargetMetadata>(breakStmt);
        var loopMd = result.GetMetadata<WhileLoopLabelMetadata>(loop)!;
        await Assert.That(breakMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(loopMd.EndLabel);
    }

    [Test]
    public async Task ContinueInsideWhile_HasContTarget() {
        var continueStmt = new ContinueStatement();
        var loop = new WhileLoop(new Constant(1L), continueStmt);
        var result = Analyze(loop);
        var continueMd = result.GetMetadata<ContinueTargetMetadata>(continueStmt);
        var loopMd = result.GetMetadata<WhileLoopLabelMetadata>(loop)!;
        await Assert.That(continueMd).IsNotNull();
        await Assert.That(continueMd!.TargetLabel).IsEqualTo(loopMd.ContLabel);
    }

    [Test]
    public async Task BreakInsideNestedWhile_ResolvesToInnerLoop() {
        var innerBreak = new BreakStatement();
        var inner = new WhileLoop(new Constant(1L), innerBreak);
        var outer = new WhileLoop(new Constant(1L), inner);
        var result = Analyze(outer);
        var breakMd = result.GetMetadata<BreakTargetMetadata>(innerBreak);
        var innerMd = result.GetMetadata<WhileLoopLabelMetadata>(inner)!;
        await Assert.That(breakMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(innerMd.EndLabel);
    }

    [Test]
    public async Task BreakInsideDoWhile_HasEndTarget() {
        var breakStmt = new BreakStatement();
        var loop = new DoWhileLoop(breakStmt, new Constant(1L));
        var result = Analyze(loop);
        var breakMd = result.GetMetadata<BreakTargetMetadata>(breakStmt);
        var loopMd = result.GetMetadata<DoWhileLoopLabelMetadata>(loop)!;
        await Assert.That(breakMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(loopMd.EndLabel);
    }

    [Test]
    public async Task BreakInsideForLoop_HasEndTarget() {
        var breakStmt = new BreakStatement();
        var v = new Variable("i");
        var loop = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            breakStmt);
        var result = Analyze(loop);
        var breakMd = result.GetMetadata<BreakTargetMetadata>(breakStmt);
        var loopMd = result.GetMetadata<ForLoopLabelMetadata>(loop)!;
        await Assert.That(breakMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(loopMd.EndLabel);
    }

    // ── Multiple constructs share label counter ─────────────────────────────

    [Test]
    public async Task WhileFollowedByIf_LabelsDoNotCollide() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var block = new Block([loop, iff]);
        var result = Analyze(block);
        var loopMd = result.GetMetadata<WhileLoopLabelMetadata>(loop)!;
        var ifMd = result.GetMetadata<IfLabelMetadata>(iff)!;
        // All five labels (loop.cont, loop.end, if.else, if.end) should be distinct.
        int[] allLabels = [loopMd.ContLabel, loopMd.EndLabel, ifMd.ElseLabel!.Value, ifMd.EndLabel];
        await Assert.That(allLabels.Distinct().Count()).IsEqualTo(4);
    }

    // ── Without LabelAssignment pass ────────────────────────────────────────

    [Test]
    public async Task WithoutLabelAssignmentPass_MetadataIsNull() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var result = AnalyzerWithoutLabels.Analyze(loop);
        var md = result.GetMetadata<WhileLoopLabelMetadata>(loop);
        await Assert.That(md).IsNull();
    }

    [Test]
    public async Task BreakOutsideLoop_NoMetadata() {
        var breakStmt = new BreakStatement();
        var result = AnalyzerWithLabels.Analyze(breakStmt);
        var md = result.GetMetadata<BreakTargetMetadata>(breakStmt);
        await Assert.That(md).IsNull();
    }

    [Test]
    public async Task ContinueOutsideLoop_NoMetadata() {
        var continueStmt = new ContinueStatement();
        var result = AnalyzerWithLabels.Analyze(continueStmt);
        var md = result.GetMetadata<ContinueTargetMetadata>(continueStmt);
        await Assert.That(md).IsNull();
    }
}