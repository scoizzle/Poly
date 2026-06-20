using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>Tests for <see cref="StackDepthAnalysisPass"/> metadata computation.</summary>
public sealed class StackDepthAnalysisTests {
    private static Analyzer AnalyzerWithDepth =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseStackDepthAnalysis()
            .Build();

    private static Analyzer AnalyzerWithoutDepth =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .Build();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task AssertDepth(Node node, int expectedEntry, int expectedExit) {
        var result = AnalyzerWithDepth.Analyze(node);
        var md = result.GetMetadata<StackDepthMetadata>(node);
        await Assert.That(md).IsNotNull();
        await Assert.That(md!.EntryDepth).IsEqualTo(expectedEntry);
        await Assert.That(md.ExitDepth).IsEqualTo(expectedExit);
    }

    // ── Leaf / simple-value nodes ───────────────────────────────────────────

    [Test]
    public async Task Constant_PushesOneValue() {
        await AssertDepth(new Constant(42L), 0, 1);
    }

    [Test]
    public async Task Variable_PushesOneValue() {
        await AssertDepth(new Variable("x"), 0, 1);
    }

    [Test]
    public async Task ThisReference_PushesOneValue() {
        await AssertDepth(new ThisReference(), 0, 1);
    }

    [Test]
    public async Task Default_PushesOneValue() {
        await AssertDepth(new Default(null), 0, 1);
    }

    [Test]
    public async Task SuspendNode_PushesOneValue() {
        await AssertDepth(new SuspendNode(new Constant(1L)), 0, 1);
    }

    [Test]
    public async Task Await_PushesOneValue() {
        await AssertDepth(new Await(new Constant(1L)), 0, 1);
    }

    [Test]
    public async Task Coalesce_PushesOneValue() {
        await AssertDepth(new Coalesce(new Constant(1L), new Constant(2L)), 0, 1);
    }

    [Test]
    public async Task NullForgiving_PushesOneValue() {
        await AssertDepth(new NullForgiving(new Constant(1L)), 0, 1);
    }

    [Test]
    public async Task TypeCast_PushesOneValue() {
        await AssertDepth(new TypeCast(new TypeReference(typeof(long).FullName!), new Constant(1L)), 0, 1);
    }

    [Test]
    public async Task TypeAs_PushesOneValue() {
        await AssertDepth(new TypeAs(new Constant("hello"), new TypeReference(typeof(string).FullName!)), 0, 1);
    }

    [Test]
    public async Task TypeIs_PushesOneValue() {
        await AssertDepth(new TypeIs(new Constant(1L), new TypeReference(typeof(long).FullName!)), 0, 1);
    }

    [Test]
    public async Task Parameter_WithDefault_PushesOneValue() {
        await AssertDepth(new Parameter("x", DefaultValue: new Constant(0L)), 0, 1);
    }

    [Test]
    public async Task Parameter_WithoutDefault_ConsumesAndPushes() {
        await AssertDepth(new Parameter("x"), 1, 1);
    }

    // ── Binary operators ────────────────────────────────────────────────────

    public static IEnumerable<(string, Func<Node, Node, Node>, int, int)> BinaryOpCases() =>
    [
        ("Add",       (l, r) => new Add(l, r),       0, 1),
        ("Subtract",  (l, r) => new Subtract(l, r),   0, 1),
        ("Multiply",  (l, r) => new Multiply(l, r),   0, 1),
        ("Divide",    (l, r) => new Divide(l, r),     0, 1),
        ("Modulo",    (l, r) => new Modulo(l, r),     0, 1),
        ("Equal",     (l, r) => new Equal(l, r),      0, 1),
        ("NotEqual",  (l, r) => new NotEqual(l, r),   0, 1),
        ("LessThan",  (l, r) => new LessThan(l, r),   0, 1),
        ("LessThanOrEqual", (l, r) => new LessThanOrEqual(l, r), 0, 1),
        ("GreaterThan",     (l, r) => new GreaterThan(l, r),     0, 1),
        ("GreaterThanOrEqual", (l, r) => new GreaterThanOrEqual(l, r), 0, 1),
        ("And",       (l, r) => new And(l, r),       0, 1),
        ("Or",        (l, r) => new Or(l, r),         0, 1),
        ("BitwiseAnd", (l, r) => new BitwiseAnd(l, r), 0, 1),
        ("BitwiseOr",  (l, r) => new BitwiseOr(l, r), 0, 1),
        ("BitwiseXor", (l, r) => new BitwiseXor(l, r), 0, 1),
        ("ShiftLeft",  (l, r) => new ShiftLeft(l, r), 0, 1),
        ("ShiftRight", (l, r) => new ShiftRight(l, r), 0, 1),
    ];

    [Test]
    [MethodDataSource(nameof(BinaryOpCases))]
    public async Task BinaryOp_PushesOneValue((string Name, Func<Node, Node, Node> Factory, int Entry, int Exit) op) {
        await AssertDepth(op.Factory(new Constant(1L), new Constant(2L)), op.Entry, op.Exit);
    }

    // ── Unary operators ─────────────────────────────────────────────────────

    [Test]
    public async Task UnaryMinus_PushesOneValue() {
        await AssertDepth(new UnaryMinus(new Constant(5L)), 0, 1);
    }

    [Test]
    public async Task Not_PushesOneValue() {
        await AssertDepth(new Not(new Constant(1L)), 0, 1);
    }

    [Test]
    public async Task BitwiseNot_PushesOneValue() {
        await AssertDepth(new BitwiseNot(new Constant(5L)), 0, 1);
    }

    // ── Member / IndexAccess / Invoke / New / NewArray ──────────────────────

    [Test]
    public async Task Member_PushesOneValue() {
        var target = new Member(new Variable("obj"), "Length");
        await AssertDepth(target, 0, 1);
    }

    [Test]
    public async Task Invoke_PushesOneValue() {
        var method = new Member(new TypeReference(typeof(Math).FullName!), "Max");
        var invoke = new Invoke(method, new Constant(1), new Constant(2));
        await AssertDepth(invoke, 0, 1);
    }

    [Test]
    public async Task Assignment_PushesOneValue() {
        await AssertDepth(new Assignment(new Variable("x"), new Constant(42L)), 0, 1);
    }

    [Test]
    public async Task Lambda_PushesOneValue() {
        var p = new Parameter("x");
        await AssertDepth(new Lambda([p], new Add(p, new Constant(1L))), 0, 1);
    }

    // ── Loops ───────────────────────────────────────────────────────────────

    [Test]
    public async Task WhileLoop_NetZero() {
        await AssertDepth(new WhileLoop(new Constant(1L), new Constant(0L)), 0, 0);
    }

    [Test]
    public async Task NestedWhileLoops_NetZero() {
        var inner = new WhileLoop(new Constant(1L), new Constant(0L));
        var outer = new WhileLoop(new Constant(1L), inner);
        await AssertDepth(outer, 0, 0);
    }

    [Test]
    public async Task DoWhileLoop_NetZero() {
        await AssertDepth(new DoWhileLoop(new Constant(0L), new Constant(1L)), 0, 0);
    }

    [Test]
    public async Task ForLoop_NetZero() {
        var v = new Variable("i");
        var loop = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            new Constant(0L));
        await AssertDepth(loop, 0, 0);
    }

    // ── Conditionals / If ───────────────────────────────────────────────────

    [Test]
    public async Task Conditional_PushesOneValue() {
        await AssertDepth(new Conditional(new Constant(1L), new Constant(10L), new Constant(20L)), 0, 1);
    }

    [Test]
    public async Task IfElse_MaxOfBranches() {
        await AssertDepth(new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L)), 0, 1);
    }

    [Test]
    public async Task IfWithoutElse_NetZero() {
        await AssertDepth(new IfStatement(new Constant(1L), new Constant(10L)), 0, 0);
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Block_SingleNode_ReturnsNodeDepth() {
        await AssertDepth(new Block([new Constant(42L)]), 0, 1);
    }

    [Test]
    public async Task Block_TwoConstants_LastValuePassesThrough() {
        // The first constant's value is popped (PopOp), the second stays.
        await AssertDepth(new Block([new Constant(10L), new Constant(20L)]), 0, 1);
    }

    [Test]
    public async Task Block_ThreeConstants_OnlyLastStays() {
        await AssertDepth(new Block([new Constant(10L), new Constant(20L), new Constant(30L)]), 0, 1);
    }

    [Test]
    public async Task Block_WithWhileLoopAndVariable_WhileDoesNotContribute() {
        var v = new Variable("x");
        var block = new Block([
            new WhileLoop(new Constant(1L), new Constant(0L)),
            new Add(v, new Constant(1L)),
        ], [v]);
        await AssertDepth(block, 0, 1);
    }

    [Test]
    public async Task Block_WithNestedWhileLoopInMiddle_WhileSkipsPopOp() {
        var v = new Variable("x");
        var block = new Block([
            new Assignment(v, new Constant(0L)),
            new WhileLoop(new LessThan(v, new Constant(10L)), new Assignment(v, new Add(v, new Constant(1L)))),
            v,
        ], [v]);
        await AssertDepth(block, 0, 1);
    }

    [Test]
    public async Task Block_WithMultipleWhiles_EachSkipsPopOp() {
        var v = new Variable("x");
        var block = new Block([
            new WhileLoop(new Constant(1L), new Constant(0L)),
            new WhileLoop(new Constant(1L), new Constant(0L)),
            v,
        ], [v]);
        await AssertDepth(block, 0, 1);
    }

    [Test]
    public async Task NestedBlock_InnerAndOuter() {
        var inner = new Block([new Constant(1L)]);
        await AssertDepth(new Block([inner, new Constant(2L)]), 0, 1);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    [Test]
    public async Task Return_WithValue_ConsumesOne() {
        await AssertDepth(new Return(new Constant(42L)), 1, 0);
    }

    [Test]
    public async Task Return_WithoutValue_NoEffect() {
        await AssertDepth(new Return(), 0, 0);
    }

    [Test]
    public async Task BreakStatement_NoEffect() {
        await AssertDepth(new BreakStatement(), 0, 0);
    }

    [Test]
    public async Task ContinueStatement_NoEffect() {
        await AssertDepth(new ContinueStatement(), 0, 0);
    }

    [Test]
    public async Task ThrowStatement_ConsumesOne() {
        await AssertDepth(new ThrowStatement(new Constant(0L)), 1, 0);
    }

    // ── IfStatement with blocks ─────────────────────────────────────────────

    [Test]
    public async Task IfElse_WithBlocks_BothBranchesHaveSameDepth() {
        await AssertDepth(
            new IfStatement(
                new Constant(1L),
                new Block([new Constant(10L)]),
                new Block([new Constant(20L)])),
            0, 1);
    }

    [Test]
    public async Task IfElse_BranchesWithDifferentDepths_UsesMax() {
        // then: Block([1, 2]) → exit=1  (PopOp removes 1, 2 stays)
        // else: Constant(3) → exit=1
        await AssertDepth(
            new IfStatement(
                new Constant(1L),
                new Block([new Constant(1L), new Constant(2L)]),
                new Constant(3L)),
            0, 1);
    }

    // ── Mixed composition ───────────────────────────────────────────────────

    [Test]
    public async Task ExpressionInsideLoop_DoesNotAffectSurrounding() {
        // while(1) { x = x + 1 } as a statement should not push
        var v = new Variable("x");
        var loop = new WhileLoop(
            new Constant(1L),
            new Assignment(v, new Add(v, new Constant(1L))));
        await AssertDepth(loop, 0, 0);
    }

    [Test]
    public async Task Block_AssignmentThenWhileThenVariable_CorrectStack() {
        var v = new Variable("x");
        var block = new Block([
            new Assignment(v, new Constant(10L)),
            new WhileLoop(new LessThan(v, new Constant(5L)), new Assignment(v, new Add(v, new Constant(1L)))),
            v,
        ], [v]);
        await AssertDepth(block, 0, 1);
    }

    // ── Without StackDepthAnalysis pass ─────────────────────────────────────

    [Test]
    public async Task WithoutStackDepthPass_MetadataIsNull() {
        var result = AnalyzerWithoutDepth.Analyze(new Constant(42L));
        var md = result.GetMetadata<StackDepthMetadata>(new Constant(42L));
        await Assert.That(md).IsNull();
    }
}