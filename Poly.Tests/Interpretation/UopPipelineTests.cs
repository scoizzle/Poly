using System.Diagnostics;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>Full pipeline tests: AST → Lowering → µops → compiled delegate → execute.
/// These are the accuracy and regression tests for the µop execution path.</summary>
public class UopPipelineTests {
    private static readonly TestTraceWriter? _traceWriter = Debugger.IsAttached ? new() : null;

    private static Bytecode LowerWith(Node node, Action<AnalyzerBuilder>? configure = null) {
        var builder = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator();
        configure?.Invoke(builder);
        var analysis = builder.Build().Analyze(node);
        return Lowering.Lower(node, analysis);
    }

    private static InterpreterResult Execute(Node node, Action<AnalyzerBuilder>? configure = null) {
        var prog = LowerWith(node, configure);
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        return Vm.Execute(state);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Basic constant expression
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Constant_EvaluatesToValue() {
        var result = Execute(new Constant(42));
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Constant_BoolTrue() {
        var result = Execute(new Constant(true));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task Constant_BoolFalse() {
        var result = Execute(new Constant(false));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task Constant_NegativeInt() {
        var result = Execute(new Constant(-7));
        await Assert.That(result.Value).IsEqualTo(-7L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Arithmetic
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Add_ComputesSum() {
        var result = Execute(new Add(new Constant(10), new Constant(20)));
        await Assert.That(result.Value).IsEqualTo(30L);
    }

    [Test]
    public async Task Sub_ComputesDifference() {
        var result = Execute(new Subtract(new Constant(100), new Constant(30)));
        await Assert.That(result.Value).IsEqualTo(70L);
    }

    [Test]
    public async Task Mul_ComputesProduct() {
        var result = Execute(new Multiply(new Constant(7), new Constant(6)));
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Div_ComputesQuotient() {
        var result = Execute(new Divide(new Constant(42), new Constant(7)));
        await Assert.That(result.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task CompoundArithmetic_MultipleOps() {
        // ((10 + 5) * 2) - (8 / 2) + 1 = 30 - 4 + 1 = 27
        var node = new Add(
            new Subtract(
                new Multiply(new Add(new Constant(10), new Constant(5)), new Constant(2)),
                new Divide(new Constant(8), new Constant(2))),
            new Constant(1));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(27L);
    }

    [Test]
    public async Task Polynomial_ConstantOnly() {
        // 3*5*5*5 + 2*5*5 + 5 + 5 = 435
        var node = new Add(
            new Add(
                new Add(
                    new Multiply(new Constant(3),
                        new Multiply(new Constant(5), new Multiply(new Constant(5), new Constant(5)))),
                    new Multiply(new Constant(2), new Multiply(new Constant(5), new Constant(5)))),
                new Constant(5)),
            new Constant(5));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(435L);
    }

    [Test]
    public async Task Modulo_ReturnsRemainder() {
        // 17 % 5 → remainder 2 (quotient popped)
        var node = new Modulo(new Constant(17), new Constant(5));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task NegativeResult_WorksCorrectly() {
        var result = Execute(new Subtract(new Constant(5), new Constant(15)));
        await Assert.That(result.Value).IsEqualTo(-10L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Unary operations
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Negate_Positive() {
        var result = Execute(new UnaryMinus(new Constant(42)));
        await Assert.That(result.Value).IsEqualTo(-42L);
    }

    [Test]
    public async Task Negate_Negative() {
        var result = Execute(new UnaryMinus(new Constant(-10)));
        await Assert.That(result.Value).IsEqualTo(10L);
    }

    [Test]
    public async Task Not_ZeroIsTrue() {
        var result = Execute(new Not(new Constant(0)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task Not_NonZeroIsFalse() {
        var result = Execute(new Not(new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Comparisons
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Equal_True() {
        var result = Execute(new Equal(new Constant(5), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task Equal_False() {
        var result = Execute(new Equal(new Constant(5), new Constant(3)));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task NotEqual_True() {
        var result = Execute(new NotEqual(new Constant(5), new Constant(3)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task NotEqual_False() {
        var result = Execute(new NotEqual(new Constant(5), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task LessThan_True() {
        var result = Execute(new LessThan(new Constant(5), new Constant(10)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThan_False() {
        var result = Execute(new LessThan(new Constant(10), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task LessThanOrEqual_Equal() {
        var result = Execute(new LessThanOrEqual(new Constant(5), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task LessThanOrEqual_Less() {
        var result = Execute(new LessThanOrEqual(new Constant(3), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThan_True() {
        var result = Execute(new GreaterThan(new Constant(10), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task GreaterThanOrEqual_Equal() {
        var result = Execute(new GreaterThanOrEqual(new Constant(5), new Constant(5)));
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Bitwise operations
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task BitwiseOr_ComputesCorrectly() {
        var result = Execute(new BitwiseOr(new Constant(5), new Constant(2)));
        await Assert.That(result.Value).IsEqualTo(7L);
    }

    [Test]
    public async Task BitwiseAnd_ComputesCorrectly() {
        var result = Execute(new BitwiseAnd(new Constant(6), new Constant(3)));
        await Assert.That(result.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task BitwiseXor_ComputesCorrectly() {
        var result = Execute(new BitwiseXor(new Constant(5), new Constant(3)));
        await Assert.That(result.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task BitwiseNot_ComputesCorrectly() {
        var result = Execute(new BitwiseNot(new Constant(0)));
        await Assert.That(result.Value).IsEqualTo(-1L);
    }

    [Test]
    public async Task ShiftLeft_ComputesCorrectly() {
        var result = Execute(new ShiftLeft(new Constant(8), new Constant(1)));
        await Assert.That(result.Value).IsEqualTo(16L);
    }

    [Test]
    public async Task ShiftRight_ComputesCorrectly() {
        var result = Execute(new ShiftRight(new Constant(16), new Constant(1)));
        await Assert.That(result.Value).IsEqualTo(8L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Conditional
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Conditional_TrueBranch() {
        var node = new Conditional(new Constant(1), new Constant(100), new Constant(0));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(100L);
    }

    [Test]
    public async Task Conditional_FalseBranch() {
        var node = new Conditional(new Constant(0), new Constant(100), new Constant(42));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Conditional_WithComparison() {
        // (5 > 10) ? 100 : 200  → 200
        var cond = new GreaterThan(new Constant(5), new Constant(10));
        var node = new Conditional(cond, new Constant(100), new Constant(200));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(200L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Block
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Block_ReturnsLastValue() {
        var node = new Block(new Constant(10), new Constant(20), new Constant(30));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(30L);
    }

    [Test]
    public async Task BlockWithArithmetic_ReturnsLast() {
        var node = new Block(
            new Add(new Constant(5), new Constant(3)),
            new Multiply(new Constant(10), new Constant(2)),
            new Divide(new Constant(100), new Constant(4)));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(25L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lambda invoke
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Lambda_NoParameters_ReturnsConstant() {
        var node = new Invoke(new Lambda([], new Constant(42)));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task Lambda_WithParameter() {
        var param = new Parameter("x", TypeReference.To<int>());
        var node = new Invoke(new Lambda([param], new Add(param, new Constant(1))), new Constant(5));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(6L);
    }

    [Test]
    public async Task Lambda_MultipleParameters() {
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var node = new Invoke(new Lambda([x, y], new Add(x, y)), new Constant(3), new Constant(4));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(7L);
    }

    [Test]
    public async Task Lambda_MultipleCalls() {
        var param = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([param], new Multiply(param, new Constant(2)));

        var prog5 = LowerWith(new Invoke(lambda, new Constant(5)));
        using var s5 = new VmState { Program = prog5, Trace = _traceWriter };
        await Assert.That(Vm.Execute(s5).Value).IsEqualTo(10L);

        var prog3 = LowerWith(new Invoke(lambda, new Constant(3)));
        using var s3 = new VmState { Program = prog3, Trace = _traceWriter };
        await Assert.That(Vm.Execute(s3).Value).IsEqualTo(6L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lambda with variables
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Lambda_WithLocals() {
        var x = new Variable("x");
        var y = new Variable("y");
        var body = new Block(
            [new Assignment(x, new Constant(5)),
             new Assignment(y, new Constant(3)),
             new Add(x, y)],
            [x, y]);
        var node = new Invoke(new Lambda([], body));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(8L);
    }

    [Test]
    public async Task Lambda_AssignmentAdd() {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var body = new Block(
            [new Assignment(sumVar, new Constant(5)),
             new Assignment(iVar, new Constant(3)),
             new Assignment(sumVar, new Add(sumVar, iVar)),
             sumVar],
            [sumVar, iVar]);
        var node = new Invoke(new Lambda([], body));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(8L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR calls
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task ClrCall_MathMax() {
        var maxMethod = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        var node = new Invoke(maxMethod, new Constant(3), new Constant(7));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(7L);
    }

    [Test]
    public async Task ClrCall_MathAbs() {
        var absMethod = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Abs));
        var node = new Invoke(absMethod, new Constant(-5));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(5L);
    }

    [Test]
    public async Task ClrCall_MathMax_ReturnsLarger() {
        var maxMethod = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        var node = new Invoke(maxMethod, new Constant(10), new Constant(3));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(10L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  While loop
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task WhileLoop_Sum1To10() {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(10)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task WhileLoop_NeverExecutes() {
        var prog = LowerWith(new Invoke(new Lambda([], new WhileLoop(new Constant(0), new Block([new Constant(42)])))));
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task WhileLoop_SingleIteration() {
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(1)),
                 new Block([new Assignment(iVar, new Add(iVar, new Constant(1)))])),
             iVar],
            [iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(2L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Do-while loop
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task DoWhileLoop_Sum1To10() {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new DoWhileLoop(
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ]),
                 new LessThanOrEqual(iVar, new Constant(10))),
             sumVar],
            [sumVar, iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  For loop
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task ForLoop_Sum1To10() {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new ForLoop(
                 new Assignment(iVar, new Constant(1)),
                 new LessThanOrEqual(iVar, new Constant(10)),
                 new Assignment(iVar, new Add(iVar, new Constant(1))),
                 new Assignment(sumVar, new Add(sumVar, iVar))),
             sumVar],
            [sumVar, iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task ForLoop_IncLocal() {
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new ForLoop(
                 new Assignment(iVar, new Constant(1)),
                 new LessThanOrEqual(iVar, new Constant(3)),
                 new Assignment(iVar, new Add(iVar, new Constant(1))),
                 new Constant(0)),
             iVar],
            [iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(4L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Nested loops
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task NestedWhileLoops_ProductTable() {
        // sum = 0; i = 1; while (i <= 3) { j = 1; while (j <= 3) { sum += i * j; j++; } i++; }
        // = (1*1+1*2+1*3)+(2*1+2*2+2*3)+(3*1+3*2+3*3) = 6+12+18 = 36
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var jVar = new Variable("j");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(3)),
                 new Block([
                     new Assignment(jVar, new Constant(1)),
                     new WhileLoop(new LessThanOrEqual(jVar, new Constant(3)),
                         new Block([
                             new Assignment(sumVar, new Add(sumVar, new Multiply(iVar, jVar))),
                             new Assignment(jVar, new Add(jVar, new Constant(1)))
                         ])),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar, jVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(36L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Two consecutive loops
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task TwoConsecutiveWhileLoops_Sum() {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(5)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             new WhileLoop(new LessThanOrEqual(iVar, new Constant(10)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Short-circuit AND
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task WhileLoop_AndCondition() {
        // while (i <= 10 AND sum < 30) { sum += i; i++; }
        // Sum until either i > 10 or sum >= 30: i=1..7 → sum=28, i=8 → sum=36≥30 stops
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(
                 new And(new LessThanOrEqual(iVar, new Constant(10)),
                         new LessThan(sumVar, new Constant(30))),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(36L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  If statement
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task IfStatement_TrueCondition() {
        var node = new Invoke(new Lambda([], new Block(
            [new IfStatement(
                 new Constant(1),
                 new Block([new Constant(42)]))],
            [])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task IfStatement_FalseCondition_SkipsBody() {
        var x = new Variable("x");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(x, new Constant(0)),
             new IfStatement(
                 new Constant(0),
                 new Block([new Assignment(x, new Constant(99))])),
             x],
            [x])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(0L);
    }

    [Test]
    public async Task IfElseStatement_TrueBranch() {
        var x = new Variable("x");
        var node = new Invoke(new Lambda([], new Block(
            [new IfStatement(
                 new Constant(1),
                 new Block([new Assignment(x, new Constant(10))]),
                 new Block([new Assignment(x, new Constant(20))])),
             x],
            [x])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(10L);
    }

    [Test]
    public async Task IfElseStatement_FalseBranch() {
        var x = new Variable("x");
        var node = new Invoke(new Lambda([], new Block(
            [new IfStatement(
                 new Constant(0),
                 new Block([new Assignment(x, new Constant(10))]),
                 new Block([new Assignment(x, new Constant(20))])),
             x],
            [x])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(20L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Stress tests
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task DeepSum_1000() {
        var node = BuildDeepSum(1000);
        var result = Execute(node);
        // sum(1..1000) = 500500
        await Assert.That(result.Value).IsEqualTo(500500L);
    }

    [Test]
    public async Task LoopSum_1000() {
        var node = BuildLoopSum(1000);
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(500500L);
    }

    [Test]
    public async Task LoopSum_10000() {
        var node = BuildLoopSum(10000);
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(50005000L);
    }

    [Test]
    public async Task ClrCallChain_100() {
        var node = BuildClrCallChain(100);
        var result = Execute(node);
        // Math.Max chain: max(1,2,3,...,100) = 100
        await Assert.That(result.Value).IsEqualTo(100L);
    }

    [Test]
    public async Task DeepSum_20000_PerfStress() {
        var node = BuildDeepSum(20000);
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(200010000L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Builders
    // ═══════════════════════════════════════════════════════════════

    private static Node BuildDeepSum(int n) {
        var values = new int[n];
        for (int i = 0; i < n; i++) values[i] = i + 1;
        return BuildBalanced(values, 0, n - 1);
    }

    private static Node BuildBalanced(int[] values, int start, int end) {
        if (start == end) return new Constant(values[start]);
        int mid = (start + end) / 2;
        return new Add(BuildBalanced(values, start, mid), BuildBalanced(values, mid + 1, end));
    }

    private static Node BuildLoopSum(int n) {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var body = new Block(
            [new Assignment(sumVar, new Constant(0)),
             new Assignment(iVar, new Constant(1)),
             new WhileLoop(
                 new LessThanOrEqual(iVar, new Constant(n)),
                 new Block([
                     new Assignment(sumVar, new Add(sumVar, iVar)),
                     new Assignment(iVar, new Add(iVar, new Constant(1)))
                 ])),
             sumVar],
            [sumVar, iVar]);
        return new Invoke(new Lambda([], body));
    }

    private static Node BuildClrCallChain(int n) {
        var maxMethod = new Member(
            new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node result = new Constant(1);
        for (int i = 2; i <= n; i++)
            result = new Invoke(maxMethod, result, new Constant(i));
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Validation / correctness
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task DivisionByZero_Throws() {
        var node = new Divide(new Constant(10), new Constant(0));
        var prog = LowerWith(node);
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        await Assert.That(() => Vm.Execute(state)).Throws<DivideByZeroException>();
    }

    [Test]
    public async Task DivRemByZero_Throws() {
        var node = new Modulo(new Constant(10), new Constant(0));
        var prog = LowerWith(node);
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        await Assert.That(() => Vm.Execute(state)).Throws<DivideByZeroException>();
    }

    [Test]
    public async Task Negate_MaxLong_DoesNotOverflow() {
        // -long.MinValue wraps to long.MinValue in unchecked context.
        // Use a known value that won't overflow: -(int.MinValue) = 2147483648.
        var node = new UnaryMinus(new Constant(int.MinValue));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(2147483648L);
    }

    [Test]
    public async Task NestedAdditions_DeepStack_NoCrash() {
        // ((((1 + 2) + 3) + 4) + ... + 100) — deep left-associative tree
        Node node = new Constant(1);
        for (int i = 2; i <= 100; i++)
            node = new Add(node, new Constant(i));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(5050L);
    }

    [Test]
    public async Task Subtract_NegativeResult() {
        var result = Execute(new Subtract(new Constant(10), new Constant(100)));
        await Assert.That(result.Value).IsEqualTo(-90L);
    }

    [Test]
    public async Task Multiply_LargeNumbers() {
        var result = Execute(new Multiply(new Constant(100000), new Constant(100000)));
        await Assert.That(result.Value).IsEqualTo(10000000000L);
    }

    [Test]
    public async Task ChainedComparisons_AllTrue() {
        // 1 < 2 < 3 < 4 is syntactic sugar for (1 < 2) < (3 < 4) in AST
        // (1 < 2) = 1, (3 < 4) = 1, so 1 < 1 = false
        // Better test: separate comparisons
        var result = Execute(new Add(
            new Add(
                new LessThan(new Constant(1), new Constant(2)),   // 1
                new LessThan(new Constant(3), new Constant(4))),  // 1
            new Equal(new Constant(5), new Constant(5))));         // 1
        await Assert.That(result.Value).IsEqualTo(3L);
    }

    [Test]
    public async Task MultiplePopsInBlock_DiscardsIntermediateResults() {
        // Block: [10; 20; 30] → final result is 30 (10 and 20 popped)
        var node = new Block(new Constant(10), new Constant(20), new Constant(30));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(30L);
    }

    [Test]
    public async Task SingleExpressionBlock_ReturnsThatExpression() {
        var prog = LowerWith(new Block(new Constant(42)));
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task BitwiseNot_Zero() {
        var result = Execute(new BitwiseNot(new Constant(0)));
        await Assert.That(result.Value).IsEqualTo(-1L);
    }

    [Test]
    public async Task BitwiseNot_MaxLong() {
        var result = Execute(new BitwiseNot(new Constant(long.MaxValue)));
        await Assert.That(result.Value).IsEqualTo(long.MinValue);
    }

    [Test]
    public async Task ShiftLeft_ByZero() {
        var result = Execute(new ShiftLeft(new Constant(42), new Constant(0)));
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task ShiftRight_ByZero() {
        var result = Execute(new ShiftRight(new Constant(42), new Constant(0)));
        await Assert.That(result.Value).IsEqualTo(42L);
    }

    [Test]
    public async Task IfElse_Chain_MultipleBranches() {
        // if (0) 10 else if (0) 20 else 30 → 30
        var outer = new IfStatement(
            new Constant(0),
            new Block([new Constant(10)]),
            new Block([new IfStatement(
                new Constant(0),
                new Block([new Constant(20)]),
                new Block([new Constant(30)]))]));
        var result = Execute(new Invoke(new Lambda([], outer)));
        await Assert.That(result.Value).IsEqualTo(30L);
    }

    [Test]
    public async Task Conditional_Nested() {
        // (1 ? (0 ? 10 : 20) : 30) → 20
        var node = new Conditional(
            new Constant(1),
            new Conditional(new Constant(0), new Constant(10), new Constant(20)),
            new Constant(30));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(20L);
    }

    [Test]
    public async Task Lambda_ReturnsLastExpressionInBlock() {
        // (() => { 1; 2; 3 })() → 3
        var node = new Invoke(new Lambda([], new Block(new Constant(1), new Constant(2), new Constant(3))));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(3L);
    }

    [Test]
    public async Task WhileLoop_FalseCondition_NeverExecutes() {
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(iVar, new Constant(99)),
             new WhileLoop(new Constant(0), new Block([new Assignment(iVar, new Constant(0))])),
             iVar],
            [iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(99L);
    }

    [Test]
    public async Task WhileLoop_ZeroIterations_ExitsCleanly() {
        // while (false) { noop } should complete without error
        var node = new Invoke(new Lambda([], new WhileLoop(new Constant(0), new Block([new Constant(0)]))));
        var prog = LowerWith(node);
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task ForLoop_ZeroIterations() {
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new ForLoop(
                 new Assignment(iVar, new Constant(1)),
                 new LessThan(iVar, new Constant(1)),  // 1 < 1 → false immediately
                 new Assignment(iVar, new Add(iVar, new Constant(1))),
                 new Block([new Constant(0)])),
             iVar],
            [iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(1L);
    }

    [Test]
    public async Task DoWhileLoop_ExecutesOnceWhenConditionFalse() {
        var iVar = new Variable("i");
        var node = new Invoke(new Lambda([], new Block(
            [new Assignment(iVar, new Constant(1)),
             new DoWhileLoop(
                 new Block([new Assignment(iVar, new Add(iVar, new Constant(1)))]),
                 new Constant(0)),
             iVar],
            [iVar])));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(2L);
    }

    [Test]
    public async Task Lambda_RepeatedInvocation_SameFunction() {
        var param = new Parameter("x", TypeReference.To<int>());
        var lambda = new Lambda([param], new Multiply(param, new Constant(3)));
        // Call it with 5, then with 7
        var prog = LowerWith(new Invoke(lambda, new Constant(5)));
        using var s5 = new VmState { Program = prog, Trace = _traceWriter };
        await Assert.That(Vm.Execute(s5).Value).IsEqualTo(15L);

        var prog2 = LowerWith(new Invoke(lambda, new Constant(7)));
        using var s7 = new VmState { Program = prog2, Trace = _traceWriter };
        await Assert.That(Vm.Execute(s7).Value).IsEqualTo(21L);
    }

    [Test]
    public async Task VoidReturn_FromLambda() {
        // Lambda that returns void (constant expression as no-op body)
        var node = new Invoke(new Lambda([], new Block([new Constant(0)])));
        var prog = LowerWith(node);
        using var state = new VmState { Program = prog, Trace = _traceWriter };
        var result = Vm.Execute(state);
        await Assert.That(state.IsComplete).IsTrue();
    }

    [Test]
    public async Task Comparison_WithImmediateConstants() {
        // Constant folding might inline these. Verify correctness.
        await Assert.That(Execute(new Equal(new Constant(0), new Constant(0))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new Equal(new Constant(1), new Constant(0))).Value).IsEqualTo(0L);
        await Assert.That(Execute(new NotEqual(new Constant(0), new Constant(0))).Value).IsEqualTo(0L);
        await Assert.That(Execute(new NotEqual(new Constant(1), new Constant(0))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new LessThan(new Constant(0), new Constant(1))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new LessThan(new Constant(1), new Constant(0))).Value).IsEqualTo(0L);
        await Assert.That(Execute(new LessThanOrEqual(new Constant(0), new Constant(0))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new LessThanOrEqual(new Constant(1), new Constant(0))).Value).IsEqualTo(0L);
        await Assert.That(Execute(new GreaterThan(new Constant(1), new Constant(0))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new GreaterThan(new Constant(0), new Constant(1))).Value).IsEqualTo(0L);
        await Assert.That(Execute(new GreaterThanOrEqual(new Constant(1), new Constant(1))).Value).IsEqualTo(1L);
        await Assert.That(Execute(new GreaterThanOrEqual(new Constant(0), new Constant(1))).Value).IsEqualTo(0L);
    }

    [Test]
    public async Task MultipleCalls_DifferentFunctions() {
        // Two different lambdas, each called once
        var add1 = new Invoke(new Lambda([new Parameter("x", TypeReference.To<int>())],
            new Add(new Variable("x"), new Constant(1))), new Constant(5));
        var double_ = new Invoke(new Lambda([new Parameter("x", TypeReference.To<int>())],
            new Multiply(new Variable("x"), new Constant(2))), new Constant(3));
        var r1 = Execute(add1);
        await Assert.That(r1.Value).IsEqualTo(6L);
        var r2 = Execute(double_);
        await Assert.That(r2.Value).IsEqualTo(6L);
    }
}