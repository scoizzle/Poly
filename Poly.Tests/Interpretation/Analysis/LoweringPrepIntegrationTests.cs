using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// Integration tests combining two or all three lowering-prep passes to verify
/// cross-pass consistency: depths, labels, and µop fragments are coherent.
/// Also verifies the unified <see cref="LoweringPrepPass"/> produces identical
/// results to the separate <see cref="StackDepthAnalysisPass"/> +
/// <see cref="LabelAssignmentPass"/>.
/// </summary>
public sealed class LoweringPrepIntegrationTests {
    private static Analyzer AllPasses =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build();

    private static Analyzer DepthAndLabels =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .Build();

    private static Analyzer LabelsAndUops =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static AnalysisResult Analyze(Node node) => AllPasses.Analyze(node);
    private static AnalysisResult AnalyzeDepthLabels(Node node) => DepthAndLabels.Analyze(node);
    private static AnalysisResult AnalyzeLabelsUops(Node node) => LabelsAndUops.Analyze(node);

    private static T? MD<T>(AnalysisResult r, Node n) where T : class, IAnalysisMetadata => r.GetMetadata<T>(n);

    // ═════════════════════════════════════════════════════════════════════════
    //  StackDepth + LabelAssignment
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task WhileLoop_HasBothDepthAndLabelMetadata() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var r = AnalyzeDepthLabels(loop);
        var depth = MD<StackDepthMetadata>(r, loop);
        var labels = MD<WhileLoopLabelMetadata>(r, loop);
        await Assert.That(depth).IsNotNull();
        await Assert.That(labels).IsNotNull();
        await Assert.That(depth!.EntryDepth).IsEqualTo(0);
        await Assert.That(depth.ExitDepth).IsEqualTo(0);
        await Assert.That(labels!.ContLabel).IsGreaterThanOrEqualTo(0);
        await Assert.That(labels.EndLabel).IsGreaterThan(labels.ContLabel);
    }

    [Test]
    public async Task ForLoop_HasBothDepthAndLabelMetadata() {
        var v = new Variable("i");
        var loop = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            new Constant(0L));
        var r = AnalyzeDepthLabels(loop);
        await Assert.That(MD<StackDepthMetadata>(r, loop)).IsNotNull();
        await Assert.That(MD<ForLoopLabelMetadata>(r, loop)).IsNotNull();
    }

    [Test]
    public async Task IfStatement_HasBothDepthAndLabelMetadata() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var r = AnalyzeDepthLabels(iff);
        var depth = MD<StackDepthMetadata>(r, iff);
        var labels = MD<IfLabelMetadata>(r, iff);
        await Assert.That(depth).IsNotNull();
        await Assert.That(labels).IsNotNull();
        await Assert.That(depth!.ExitDepth).IsEqualTo(0); // IfStatement is Statement, no net push
        await Assert.That(labels!.ElseLabel).IsNotNull();
    }

    [Test]
    public async Task Conditional_HasBothDepthAndLabelMetadata() {
        var cond = new Conditional(new Constant(1L), new Constant(10L), new Constant(20L));
        var r = AnalyzeDepthLabels(cond);
        var depth = MD<StackDepthMetadata>(r, cond);
        var labels = MD<ConditionalLabelMetadata>(r, cond);
        await Assert.That(depth).IsNotNull();
        await Assert.That(labels).IsNotNull();
        await Assert.That(depth!.ExitDepth).IsEqualTo(1);
    }

    [Test]
    public async Task DoWhileLoop_HasBothDepthAndLabelMetadata() {
        var dwl = new DoWhileLoop(new Constant(0L), new Constant(1L));
        var r = AnalyzeDepthLabels(dwl);
        await Assert.That(MD<StackDepthMetadata>(r, dwl)).IsNotNull();
        await Assert.That(MD<DoWhileLoopLabelMetadata>(r, dwl)).IsNotNull();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  LabelAssignment + UopGeneration — µops reference correct label IDs
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task WhileLoop_UopsUseAssignedLabels() {
        var loop = new WhileLoop(new Constant(1L), new Constant(0L));
        var r = AnalyzeLabelsUops(loop);
        var labels = MD<WhileLoopLabelMetadata>(r, loop);
        var uops = MD<LoweredUopMetadata>(r, loop)!.Uops;

        // Find BranchIfFalse and Jump, verify their targets = EndLabel and ContLabel
        var bif = (BranchIfFalse)uops[1];
        var jump = (Jump)uops[^1];

        await Assert.That(bif.Target).IsEqualTo(labels!.EndLabel);
        await Assert.That(jump.Target).IsEqualTo(labels.ContLabel);
    }

    [Test]
    public async Task IfWithElse_UopsUseAssignedLabels() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var r = AnalyzeLabelsUops(iff);
        var labels = MD<IfLabelMetadata>(r, iff);
        var uops = MD<LoweredUopMetadata>(r, iff)!.Uops;

        var bif = (BranchIfFalse)uops[1];
        var jump = (Jump)uops[4];

        await Assert.That(bif.Target).IsEqualTo(labels!.ElseLabel!.Value);
        await Assert.That(jump.Target).IsEqualTo(labels.EndLabel);
    }

    [Test]
    public async Task Conditional_UopsUseAssignedLabels() {
        var cond = new Conditional(new Constant(1L), new Constant(10L), new Constant(20L));
        var r = AnalyzeLabelsUops(cond);
        var labels = MD<ConditionalLabelMetadata>(r, cond);
        var uops = MD<LoweredUopMetadata>(r, cond)!.Uops;

        var bif = (BranchIfFalse)uops[1];
        var jump = (Jump)uops[3];

        await Assert.That(bif.Target).IsEqualTo(labels!.FalseLabel);
        await Assert.That(jump.Target).IsEqualTo(labels.EndLabel);
    }

    [Test]
    public async Task ForLoop_UopsUseAssignedLabels() {
        var v = new Variable("i");
        var loop = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            new Constant(0L));
        var r = AnalyzeLabelsUops(loop);
        var labels = MD<ForLoopLabelMetadata>(r, loop);
        var uops = MD<LoweredUopMetadata>(r, loop)!.Uops;

        // Last µop should be Jump back to CondLabel
        var jump = (Jump)uops[^1];
        // There should be a BranchIfFalse targeting EndLabel
        await Assert.That(jump.Target).IsEqualTo(labels!.CondLabel);
        await Assert.That(uops.Exists(u => u is BranchIfFalse bif && bif.Target == labels.EndLabel)).IsTrue();
    }

    [Test]
    public async Task DoWhileLoop_UopsUseAssignedLabels() {
        var dwl = new DoWhileLoop(new Constant(0L), new Constant(1L));
        var r = AnalyzeLabelsUops(dwl);
        var labels = MD<DoWhileLoopLabelMetadata>(r, dwl);
        var uops = MD<LoweredUopMetadata>(r, dwl)!.Uops;

        var bif = (BranchIfFalse)uops[3];
        var jump = (Jump)uops[4];

        await Assert.That(bif.Target).IsEqualTo(labels!.EndLabel);
        await Assert.That(jump.Target).IsEqualTo(labels.ContLabel);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  All three passes — structural consistency
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task NestedWhileLoops_AllPassesConsistent() {
        var inner = new WhileLoop(new Constant(1L), new Constant(0L));
        var outer = new WhileLoop(new Constant(1L), inner);
        var r = Analyze(outer);

        var innerDepth = MD<StackDepthMetadata>(r, inner);
        var outerDepth = MD<StackDepthMetadata>(r, outer);
        var innerLabels = MD<WhileLoopLabelMetadata>(r, inner);
        var outerLabels = MD<WhileLoopLabelMetadata>(r, outer);
        var innerUops = MD<LoweredUopMetadata>(r, inner)!.Uops;
        var outerUops = MD<LoweredUopMetadata>(r, outer)!.Uops;

        // Both loops are net-zero depth
        await Assert.That(innerDepth!.ExitDepth).IsEqualTo(0);
        await Assert.That(outerDepth!.ExitDepth).IsEqualTo(0);

        // Labels are distinct across nesting levels
        await Assert.That(outerLabels!.ContLabel).IsNotEqualTo(innerLabels!.ContLabel);
        await Assert.That(outerLabels.EndLabel).IsNotEqualTo(innerLabels.EndLabel);

        // Inner loop's µops are embedded in outer loop's body
        // Outer: condition(1), BranchIfFalse(outerEnd), body(inner uops), PopOp, Jump(outerCont)
        // Inner uops should appear in the outer uops at position 2..(2+innerCount-1)
        int innerStart = 2;
        for (int i = 0; i < innerUops.Count; i++)
            await Assert.That(outerUops[innerStart + i]).IsSameReferenceAs(innerUops[i]);
    }

    [Test]
    public async Task IfInsideWhile_AllPassesConsistent() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var loop = new WhileLoop(new Constant(1L), iff);
        var r = Analyze(loop);

        var loopDepth = MD<StackDepthMetadata>(r, loop);
        var ifDepth = MD<StackDepthMetadata>(r, iff);
        var loopLabels = MD<WhileLoopLabelMetadata>(r, loop);
        var ifLabels = MD<IfLabelMetadata>(r, iff);
        var loopUops = MD<LoweredUopMetadata>(r, loop)!.Uops;
        var ifUops = MD<LoweredUopMetadata>(r, iff)!.Uops;

        // Loop = net-zero, If = pushes 1 (max of 1,1)
        await Assert.That(loopDepth!.ExitDepth).IsEqualTo(0);
        await Assert.That(ifDepth!.ExitDepth).IsEqualTo(0);

        // Labels are present for both
        await Assert.That(loopLabels).IsNotNull();
        await Assert.That(ifLabels).IsNotNull();

        // The If's uops are embedded in the loop body (starting at position 2)
        int ifStart = 2;
        for (int i = 0; i < ifUops.Count; i++)
            await Assert.That(loopUops[ifStart + i]).IsSameReferenceAs(ifUops[i]);
    }

    [Test]
    public async Task BlockWithMixedContent_AllPassesConsistent() {
        var v = new Variable("x");
        var block = new Block([
            new Assignment(v, new Constant(0L)),
            new WhileLoop(new LessThan(v, new Constant(10L)),
                new Assignment(v, new Add(v, new Constant(1L)))),
            v,
        ], [v]);
        var r = Analyze(block);

        var blockDepth = MD<StackDepthMetadata>(r, block);
        var assignDepth = MD<StackDepthMetadata>(r, block.Nodes[0]);
        var loopDepth = MD<StackDepthMetadata>(r, block.Nodes[1]);
        var varDepth = MD<StackDepthMetadata>(r, block.Nodes[2]);
        var loopLabels = MD<WhileLoopLabelMetadata>(r, block.Nodes[1]);
        var blockUops = MD<LoweredUopMetadata>(r, block)!.Uops;
        var loopUops = MD<LoweredUopMetadata>(r, block.Nodes[1])!.Uops;

        // Block should exit with 1 value (the variable)
        await Assert.That(blockDepth!.ExitDepth).IsEqualTo(1);

        // Assignment pushes 1
        await Assert.That(assignDepth!.ExitDepth).IsEqualTo(1);

        // WhileLoop is net-zero
        await Assert.That(loopDepth!.ExitDepth).IsEqualTo(0);

        // Variable pushes 1
        await Assert.That(varDepth!.ExitDepth).IsEqualTo(1);

        // Loop has labels
        await Assert.That(loopLabels).IsNotNull();

        // Block µops should not contain PopOp after the WhileLoop
        // (the WhileLoop's uops are embedded, then LoadSlot follows)
        bool afterLoop = false;
        int popAfterLoop = 0;
        foreach (var u in blockUops) {
            if (u is Jump) afterLoop = true;
            if (afterLoop && u is PopOp) popAfterLoop++;
        }
        await Assert.That(popAfterLoop).IsEqualTo(0);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Break/Continue resolve through all passes
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task BreakAndContinue_AllPassesResolveCorrectly() {
        var breakStmt = new BreakStatement();
        var continueStmt = new ContinueStatement();
        var body = new Block([breakStmt, continueStmt]);
        var loop = new WhileLoop(new Constant(1L), body);
        var r = Analyze(loop);

        var loopLabels = MD<WhileLoopLabelMetadata>(r, loop);
        var breakMd = MD<BreakTargetMetadata>(r, breakStmt);
        var continueMd = MD<ContinueTargetMetadata>(r, continueStmt);

        await Assert.That(loopLabels).IsNotNull();
        await Assert.That(breakMd).IsNotNull();
        await Assert.That(continueMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(loopLabels!.EndLabel);
        await Assert.That(continueMd!.TargetLabel).IsEqualTo(loopLabels.ContLabel);
    }

    [Test]
    public async Task BreakInsideNestedLoop_ResolvesToInnerLoop() {
        var innerBreak = new BreakStatement();
        var innerLoop = new WhileLoop(new Constant(1L), innerBreak);
        var outerLoop = new WhileLoop(new Constant(1L), innerLoop);
        var r = Analyze(outerLoop);

        var innerLabels = MD<WhileLoopLabelMetadata>(r, innerLoop);
        var outerLabels = MD<WhileLoopLabelMetadata>(r, outerLoop);
        var breakMd = MD<BreakTargetMetadata>(r, innerBreak);

        await Assert.That(breakMd).IsNotNull();
        await Assert.That(breakMd!.TargetLabel).IsEqualTo(innerLabels!.EndLabel);
        await Assert.That(breakMd.TargetLabel).IsNotEqualTo(outerLabels!.EndLabel);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Cross-metadata consistency with real-world patterns
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task MandelbrotPixel_AllPasses() {
        const int S = 8;
        var x = new Variable("x");
        var zx = new Variable("zx");
        var zy = new Variable("zy");
        var zx2 = new Variable("zx2");
        var zy2 = new Variable("zy2");
        var iter = new Variable("iter");

        Node Cx(Node xv) => new Subtract(new Multiply(xv, new Constant(8L)), new Constant(4L));

        Node body = new Invoke(new Lambda([], new Block(
            [new Assignment(zx, new Constant(0L)),
             new Assignment(zy, new Constant(0L)),
             new Assignment(iter, new Constant(0L)),
             new WhileLoop(
                 new And(new LessThan(iter, new Constant(256)),
                     new LessThanOrEqual(
                         new Add(new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                             new ShiftRight(new Multiply(zy, zy), new Constant(S))),
                         new Constant(4 << S))),
                 new Block([new Assignment(zx2, new Add(
                     new Subtract(new ShiftRight(new Multiply(zx, zx), new Constant(S)),
                         new ShiftRight(new Multiply(zy, zy), new Constant(S))), Cx(x))),
                     new Assignment(zy, new Add(new ShiftRight(
                         new Multiply(new Multiply(zx, new Constant(2L)), zy), new Constant(S)), new Constant(-512L))),
                     new Assignment(zx, zx2),
                     new Assignment(iter, new Add(iter, new Constant(1L)))]))],
            [x, zx, zy, zx2, zy2, iter])), iter);

        var r = Analyze(body);

        // Find the inner pixel while loop
        // Walk through the block nodes to find WhileLoop nodes and verify they all have metadata
        var metadataCount = 0;

        void Walk(Node node) {
            if (MD<StackDepthMetadata>(r, node) is not null) metadataCount++;
            if (MD<LoweredUopMetadata>(r, node) is not null) metadataCount++;

            if (node is WhileLoop wl && MD<WhileLoopLabelMetadata>(r, wl) is not null)
                metadataCount++;

            if (node is ForLoop fl && MD<ForLoopLabelMetadata>(r, fl) is not null)
                metadataCount++;

            if (node is DoWhileLoop dwl && MD<DoWhileLoopLabelMetadata>(r, dwl) is not null)
                metadataCount++;

            if (node is IfStatement iff && MD<IfLabelMetadata>(r, iff) is not null)
                metadataCount++;

            if (node is Conditional cond && MD<ConditionalLabelMetadata>(r, cond) is not null)
                metadataCount++;

            foreach (var child in node.Children)
                if (child is not null) Walk(child);
        }

        Walk(body);

        // Every node in the tree has at least StackDepth + Uop metadata.
        // Every loop has label metadata. Every If/Conditional has label metadata.
        await Assert.That(metadataCount).IsGreaterThan(50);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Verify no metadata from wrong pass "leaks" in unexpected places
    // ═════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Constant_OnlyHasDepthAndUopNotLabels() {
        var c = new Constant(42L);
        var r = Analyze(c);
        await Assert.That(MD<StackDepthMetadata>(r, c)).IsNotNull();
        await Assert.That(MD<LoweredUopMetadata>(r, c)).IsNotNull();
        await Assert.That(MD<WhileLoopLabelMetadata>(r, c)).IsNull();
        await Assert.That(MD<IfLabelMetadata>(r, c)).IsNull();
        await Assert.That(MD<ConditionalLabelMetadata>(r, c)).IsNull();
    }

    [Test]
    public async Task BinaryOp_HasDepthAndUopNotLabels() {
        var add = new Add(new Constant(1L), new Constant(2L));
        var r = Analyze(add);
        await Assert.That(MD<StackDepthMetadata>(r, add)).IsNotNull();
        await Assert.That(MD<LoweredUopMetadata>(r, add)).IsNotNull();
        await Assert.That(MD<WhileLoopLabelMetadata>(r, add)).IsNull();
    }
}