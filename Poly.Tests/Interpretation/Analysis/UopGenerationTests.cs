using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm.Instructions;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>Tests for <see cref="UopGenerationPass"/> µop fragment generation.</summary>
public sealed class UopGenerationTests {
    // Full pipeline with constant folding — folded expressions collapse to LoadConst.
    private static Analyzer AnalyzerWithFolding =>
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

    // Without constant folding — preserves the original µop structure.
    private static Analyzer AnalyzerNoFolding =>
        new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .UseUopGeneration()
            .Build();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<Instruction> GetUops(Node node) {
        var result = AnalyzerWithFolding.Analyze(node);
        return result.GetMetadata<LoweredUopMetadata>(node)?.Uops ?? [];
    }

    private static List<Instruction> GetUopsNoFold(Node node) {
        var result = AnalyzerNoFolding.Analyze(node);
        return result.GetMetadata<LoweredUopMetadata>(node)?.Uops ?? [];
    }

    // ── Constants ──────────────────────────────────────────────────────────

    [Test]
    public async Task Constant42_EmitsLoadConst() {
        var uops = GetUops(new Constant(42L));
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(((LoadConst)uops[0]).Value).IsEqualTo(42L);
    }

    [Test]
    public async Task ConstantTrue_EmitsLoadConst1() {
        var uops = GetUops(new Constant(true));
        await Assert.That(((LoadConst)uops[0]).Value).IsEqualTo(1L);
    }

    [Test]
    public async Task ConstantFalse_EmitsLoadConst0() {
        var uops = GetUops(new Constant(false));
        await Assert.That(((LoadConst)uops[0]).Value).IsEqualTo(0L);
    }

    [Test]
    public async Task ConstantInt_EmitsLoadConst() {
        var uops = GetUops(new Constant(5));
        await Assert.That(((LoadConst)uops[0]).Value).IsEqualTo(5L);
    }

    // ── Variables / Parameters ─────────────────────────────────────────────

    [Test]
    public async Task Variable_EmitsLoadSlot() {
        var uops = GetUops(new Variable("x"));
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<LoadSlot>();
        await Assert.That(((LoadSlot)uops[0]).Offset).IsEqualTo(1); // slot 1 = argSlots + 1 + 0
    }

    // ── Binary ops ─────────────────────────────────────────────────────────

    [Test]
    public async Task Add_EmitsTwoLoadConstsThenBinOp() {
        var uops = GetUopsNoFold(new Add(new Constant(1L), new Constant(2L)));
        await Assert.That(uops).Count().IsEqualTo(3);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(uops[1]).IsTypeOf<LoadConst>();
        await Assert.That(uops[2]).IsTypeOf<BinOp>();
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Add);
    }

    [Test]
    public async Task Subtract_EmitsBinOpSub() {
        var uops = GetUopsNoFold(new Subtract(new Constant(10L), new Constant(3L)));
        await Assert.That(uops).Count().IsEqualTo(3);
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Sub);
    }

    [Test]
    public async Task Multiply_EmitsBinOpMul() {
        var uops = GetUopsNoFold(new Multiply(new Constant(2L), new Constant(3L)));
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Mul);
    }

    [Test]
    public async Task LessThan_EmitsBinOpLt() {
        var uops = GetUopsNoFold(new LessThan(new Constant(1L), new Constant(2L)));
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Lt);
    }

    [Test]
    public async Task Equal_EmitsBinOpEq() {
        var uops = GetUopsNoFold(new Equal(new Constant(1L), new Constant(1L)));
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Eq);
    }

    [Test]
    public async Task BitwiseAnd_EmitsBinOpAnd() {
        var uops = GetUops(new BitwiseAnd(new Constant(3L), new Constant(1L)));
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.And);
    }

    [Test]
    public async Task ShiftRight_EmitsBinOpShr() {
        var uops = GetUops(new ShiftRight(new Constant(8L), new Constant(2L)));
        await Assert.That(((BinOp)uops[2]).Kind).IsEqualTo(BinOpKind.Shr);
    }

    // ── Unary ops ──────────────────────────────────────────────────────────

    [Test]
    public async Task UnaryMinus_EmitsLoadConstThenBinOpSub() {
        var uops = GetUopsNoFold(new UnaryMinus(new Constant(5L)));
        await Assert.That(uops).Count().IsEqualTo(2);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(uops[1]).IsTypeOf<BinOp>();
        await Assert.That(((BinOp)uops[1]).Immediate).IsEqualTo(0);
    }

    [Test]
    public async Task Not_EmitsLoadConstThenUnaryOpNot() {
        var uops = GetUops(new Not(new Constant(1L)));
        await Assert.That(uops).Count().IsEqualTo(2);
        await Assert.That(uops[1]).IsTypeOf<UnaryOp>();
        await Assert.That(((UnaryOp)uops[1]).Kind).IsEqualTo(UnaryOpKind.Not);
    }

    // ── Assignment ─────────────────────────────────────────────────────────

    [Test]
    public async Task AssignConstant_EmitsLoadConstStoreSlotLoadSlot() {
        var v = new Variable("x");
        var uops = GetUops(new Assignment(v, new Constant(42L)));
        await Assert.That(uops).Count().IsEqualTo(3);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();       // value
        await Assert.That(uops[1]).IsTypeOf<StoreSlot>();       // store
        await Assert.That(uops[2]).IsTypeOf<LoadSlot>();        // push back
    }

    // ── Block ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Block_SingleConstant_EmitsLoadConst() {
        var uops = GetUops(new Block([new Constant(42L)]));
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
    }

    [Test]
    public async Task Block_TwoConstants_HasPopOpBetween() {
        var uops = GetUops(new Block([new Constant(1L), new Constant(2L)]));
        await Assert.That(uops).Count().IsEqualTo(3);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(uops[1]).IsTypeOf<PopOp>();
        await Assert.That(uops[2]).IsTypeOf<LoadConst>();
    }

    [Test]
    public async Task Block_ThreeConstants_HasTwoPopOps() {
        var uops = GetUops(new Block([new Constant(1L), new Constant(2L), new Constant(3L)]));
        await Assert.That(uops).Count().IsEqualTo(5);
        await Assert.That(uops[1]).IsTypeOf<PopOp>();
        await Assert.That(uops[3]).IsTypeOf<PopOp>();
    }

    [Test]
    public async Task Block_WithWhileLoop_NoPopOpAfterWhile() {
        var v = new Variable("x");
        var block = new Block([
            new WhileLoop(new Constant(1L), new Constant(0L)),
            v,
        ], [v]);
        var uops = GetUops(block);
        // WhileLoop emits: condition, BranchIfFalse, body, PopOp, Jump — no PopOp from Block after it.
        // Then LoadSlot for v.
        // Total: 3 (cond) + 1 (BranchIfFalse) + 1 (body) + 1 (PopOp) + 1 (Jump) + 1 (LoadSlot) = 8
        // The last µop should be LoadSlot(v), not PopOp.
        await Assert.That(uops[^1]).IsTypeOf<LoadSlot>();
        // No PopOp after the WhileLoop (before the final LoadSlot).
        // The last uop should be LoadSlot(v), not PopOp.
        bool hasPopOpAfterWhile = false;
        bool sawJump = false;
        foreach (var u in uops) {
            if (u is Jump) sawJump = true;
            if (sawJump && u is PopOp) hasPopOpAfterWhile = true;
        }
        await Assert.That(hasPopOpAfterWhile).IsFalse();
    }

    [Test]
    public async Task Block_WithVariableDeclaration_EmitsInit() {
        var v = new Variable("x");
        var block = new Block([v], [v]);
        var uops = GetUops(block);
        // LoadConst(0) + StoreSlot(1) + LoadSlot(1)
        await Assert.That(uops).Count().IsEqualTo(3);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(((LoadConst)uops[0]).Value).IsEqualTo(0L);
        await Assert.That(uops[1]).IsTypeOf<StoreSlot>();
        await Assert.That(uops[2]).IsTypeOf<LoadSlot>();
    }

    // ── IfStatement ────────────────────────────────────────────────────────

    [Test]
    public async Task IfWithElse_EmitsBranchIfFalseAndJump() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L), new Constant(20L));
        var uops = GetUops(iff);
        // Condition (1), BranchIfFalse(else), then body (1), PopOp, Jump(end), LabelMarker(else), else body (1), PopOp, LabelMarker(end)
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(uops[1]).IsTypeOf<BranchIfFalse>();
        await Assert.That(uops[2]).IsTypeOf<LoadConst>();  // then = 10
        await Assert.That(uops[3]).IsTypeOf<PopOp>();
        await Assert.That(uops[4]).IsTypeOf<Jump>();
        await Assert.That(uops[6]).IsTypeOf<LoadConst>();  // else = 20 (after LabelMarker at 5)
        await Assert.That(uops[7]).IsTypeOf<PopOp>();
    }

    [Test]
    public async Task IfWithoutElse_EmitsBranchIfFalseOnly() {
        var iff = new IfStatement(new Constant(1L), new Constant(10L));
        var uops = GetUops(iff);
        // Condition (1), BranchIfFalse(end), then body (1), PopOp, LabelMarker(end)
        await Assert.That(uops).Count().IsEqualTo(5);
        await Assert.That(uops[1]).IsTypeOf<BranchIfFalse>();
        await Assert.That(uops[2]).IsTypeOf<LoadConst>();
        await Assert.That(uops[3]).IsTypeOf<PopOp>();
    }

    // ── WhileLoop ──────────────────────────────────────────────────────────

    [Test]
    public async Task WhileLoop_EmitsConditionBranchBodyPopJump() {
        var wl = new WhileLoop(new Constant(1L), new Constant(0L));
        var uops = GetUops(wl);
        // LabelMarker(cont), condition(1), BranchIfFalse(end), body(1), PopOp, Jump(cont), LabelMarker(end)
        await Assert.That(uops).Count().IsEqualTo(7);
        await Assert.That(uops[1]).IsTypeOf<LoadConst>();       // condition
        await Assert.That(uops[2]).IsTypeOf<BranchIfFalse>();   // branch
        await Assert.That(uops[3]).IsTypeOf<LoadConst>();       // body
        await Assert.That(uops[4]).IsTypeOf<PopOp>();
        await Assert.That(uops[5]).IsTypeOf<Jump>();
    }

    // ── DoWhileLoop ────────────────────────────────────────────────────────

    [Test]
    public async Task DoWhileLoop_EmitsBodyPopConditionBranchJump() {
        var dwl = new DoWhileLoop(new Constant(0L), new Constant(1L));
        var uops = GetUops(dwl);
        // LabelMarker(cont), body(1), PopOp, condition(1), BranchIfFalse(end), Jump(cont), LabelMarker(end)
        await Assert.That(uops).Count().IsEqualTo(7);
        await Assert.That(uops[1]).IsTypeOf<LoadConst>();       // body
        await Assert.That(uops[2]).IsTypeOf<PopOp>();
        await Assert.That(uops[3]).IsTypeOf<LoadConst>();       // condition
        await Assert.That(uops[4]).IsTypeOf<BranchIfFalse>();
        await Assert.That(uops[5]).IsTypeOf<Jump>();
    }

    // ── ForLoop ────────────────────────────────────────────────────────────

    [Test]
    public async Task ForLoop_EmitsInitCondBranchBodyIncJump() {
        var v = new Variable("i");
        var fl = new ForLoop(
            new Assignment(v, new Constant(0L)),
            new LessThan(v, new Constant(10L)),
            new Assignment(v, new Add(v, new Constant(1L))),
            new Constant(0L));
        var uops = GetUops(fl);
        // init(3), PopOp, cond(2), BranchIfFalse(end), body(1), PopOp, inc(3), PopOp, Jump(cond)
        await Assert.That(uops[^1]).IsTypeOf<Jump>();            // last is Jump back
        // Has BranchIfFalse
        await Assert.That(uops.Exists(i => i is BranchIfFalse)).IsTrue();
    }

    // ── Conditional ────────────────────────────────────────────────────────

    [Test]
    public async Task Conditional_EmitsBranchAndJump() {
        var cond = new Conditional(new Constant(1L), new Constant(10L), new Constant(20L));
        var uops = GetUops(cond);
        // cond(1), BranchIfFalse(false), ifTrue(1), Jump(end), LabelMarker(false), ifFalse(1), LabelMarker(end)
        await Assert.That(uops).Count().IsEqualTo(7);
        await Assert.That(uops[1]).IsTypeOf<BranchIfFalse>();
        await Assert.That(uops[3]).IsTypeOf<Jump>();
        await Assert.That(uops[5]).IsTypeOf<LoadConst>();       // ifFalse
    }

    // ── Return ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ReturnWithValue_EmitsValueThenReturnOp() {
        var ret = new Return(new Constant(42L));
        var uops = GetUops(ret);
        await Assert.That(uops).Count().IsEqualTo(2);
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
        await Assert.That(uops[1]).IsTypeOf<ReturnOp>();
    }

    [Test]
    public async Task ReturnWithoutValue_EmitsReturnOpOnly() {
        var ret = new Return();
        var uops = GetUops(ret);
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<ReturnOp>();
    }

    // ── Lambda ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Lambda_EmitsBodyThenReturnOp() {
        var p = new Parameter("x");
        var lam = new Lambda([p], new Add(p, new Constant(1L)));
        var uops = GetUops(lam);
        // body: LoadSlot(x), LoadConst(1), BinOp(Add)
        // then ReturnOp
        await Assert.That(uops[^1]).IsTypeOf<ReturnOp>();
        await Assert.That(uops).Count().IsEqualTo(4);
    }

    // ── Invoke (CLR method) ────────────────────────────────────────────────

    [Test]
    public async Task InvokeClrMethod_EmitsCallExternalDirect() {
        var method = new Member(new TypeReference(typeof(Math).FullName!), "Max");
        var inv = new Invoke(method, new Constant(1), new Constant(2));
        var uops = GetUops(inv);
        await Assert.That(uops[^1]).IsTypeOf<CallExternalDirect>();
        await Assert.That(uops).Count().IsEqualTo(3); // 2 args + call
    }

    // ── Break / Continue ───────────────────────────────────────────────────

    [Test]
    public async Task Break_EmitsJump0() {
        var uops = GetUops(new BreakStatement());
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<Jump>();
    }

    [Test]
    public async Task Continue_EmitsJump0() {
        var uops = GetUops(new ContinueStatement());
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<Jump>();
    }

    // ── Without UopGeneration pass ─────────────────────────────────────────

    [Test]
    public async Task WithoutUopGenerationPass_MetadataIsNull() {
        var analyzer = new AnalyzerBuilder()
            .UseTypeAndMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator()
            .UseLoweringPreparation()
            .Build();
        var result = analyzer.Analyze(new Constant(42L));
        var md = result.GetMetadata<LoweredUopMetadata>(new Constant(42L));
        await Assert.That(md).IsNull();
    }

    // ── Throw ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Throw_EmitsExceptionUops() {
        var uops = GetUops(new ThrowStatement(new Constant(0L)));
        await Assert.That(uops).Count().IsEqualTo(1).And.HasSingleItem();
        await Assert.That(uops[0]).IsTypeOf<LoadConst>();
    }
}