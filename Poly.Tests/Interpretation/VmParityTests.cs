using System.IO;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class VmParityTests {
    private static object? Normalize(object? val) {
        if (val is bool b) return b ? 1 : 0;
        return val;
    }

    private static InterpreterResult EvaluateVm(Node node) {
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseLambdaReturnTypeResolution()
            .UseStackDepthAnalysis()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        var program = Lowering.Lower(node, analysis);
        using var state = new VmState { Program = program };
        return Vm.Execute(state);
    }

    private static async Task AssertParityInt(Node node, int expected) {
        var vm = EvaluateVm(node);
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That((int)Normalize(vm.Value)!).IsEqualTo(expected);
    }

    [Test]
    public async Task And_LeftFalse_ShortCircuitsRight_VmOnly() {
        var vm = EvaluateVm(new And(new Constant(0), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(0);
    }

    [Test]
    public async Task Or_LeftTrue_ShortCircuitsRight_VmOnly() {
        var vm = EvaluateVm(new Or(new Constant(1), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(1);
    }

    [Test]
    public async Task TypeCast_ReturnsOperandValue_VmOnly() {
        var vm = EvaluateVm(new TypeCast(new Constant(42), TypeReference.To<string>()));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
    }

    [Test]
    public async Task TypeIs_String_Is_Int_ReturnsFalse_VmOnly() {
        var vm = EvaluateVm(new TypeIs(new Constant("text"), TypeReference.To<int>()));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(Normalize(vm.Value)).IsEqualTo(0);
    }

    [Test]
    public async Task TypeIs_Null_ReturnsFalse_VmOnly() {
        var vm = EvaluateVm(new TypeIs(new Constant(null), TypeReference.To<string>()));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(Normalize(vm.Value)).IsEqualTo(0);
    }

    [Test]
    public async Task Divide_ByZero_Throws_VmOnly() {
        var vm = EvaluateVm(new Divide(new Constant(10), new Constant(0)));
        await Assert.That(vm.IsSignal).IsTrue();
        await Assert.That(vm.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Throw);
    }

    [Test]
    public async Task ForLoop_ConditionFalse_DoesNotExecuteBody_VmOnly() {
        var x = new Variable("x");
        var body = new Block([
            new Assignment(x, new Constant(0)),
            new ForLoop(null, new Constant(0), null, new Assignment(x, new Constant(99))),
            x
        ], [x]);
        var lambda = new Lambda([], body);
        var vm = EvaluateVm(new Invoke(lambda, []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(Normalize(vm.Value)).IsEqualTo(0);
    }

    [Test]
    public async Task Constant_Int_Parity() {
        await AssertParityInt(new Constant(42), 42);
    }

    [Test]
    public async Task Constant_NegativeInt_Parity() {
        await AssertParityInt(new Constant(-7), -7);
    }

    [Test]
    public async Task Constant_String_Parity() {
        var vm = EvaluateVm(new Constant("hello"));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo("hello");
    }

    [Test]
    public async Task Constant_Double_Parity() {
        var vm = EvaluateVm(new Constant(3.14));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(3.14);
    }

    [Test]
    public async Task Add_Parity() {
        await AssertParityInt(new Add(new Constant(3), new Constant(4)), 7);
    }

    [Test]
    public async Task Add_StringConcat_Parity() {
        var vm = EvaluateVm(new Add(new Constant("Hello "), new Constant("World")));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo("Hello World");
    }

    [Test]
    public async Task Add_StringAndInt_Parity() {
        var vm = EvaluateVm(new Add(new Constant("Count: "), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo("Count: 42");
    }

    [Test]
    public async Task Sub_Parity() {
        await AssertParityInt(new Subtract(new Constant(10), new Constant(3)), 7);
    }

    [Test]
    public async Task Mul_Parity() {
        await AssertParityInt(new Multiply(new Constant(6), new Constant(7)), 42);
    }

    [Test]
    public async Task Div_Parity() {
        await AssertParityInt(new Divide(new Constant(42), new Constant(6)), 7);
    }

    [Test]
    public async Task Mod_Parity() {
        await AssertParityInt(new Modulo(new Constant(10), new Constant(3)), 1);
    }

    [Test]
    public async Task Neg_Parity() {
        await AssertParityInt(new UnaryMinus(new Constant(5)), -5);
    }

    [Test]
    public async Task IfStatement_TrueBranch_Parity() {
        var vm = EvaluateVm(new IfStatement(new Constant(1), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
    }

    [Test]
    public async Task IfStatement_FalseWithElse_Parity() {
        var vm = EvaluateVm(new IfStatement(new Constant(0), new Constant(1), new Constant(2)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(2);
    }

    [Test]
    public async Task IfStatement_FalseNoElse_Parity() {
        var vm = EvaluateVm(new IfStatement(new Constant(0), new Constant(1)));
        await Assert.That(vm.HasValue).IsFalse();
    }

    [Test]
    public async Task WhileLoop_FalseCondition_Parity() {
        var vm = EvaluateVm(new WhileLoop(new Constant(0), new Constant(42)));
        await Assert.That(vm.HasValue).IsFalse();
    }

    [Test]
    public async Task ForLoop_FalseCondition_Parity() {
        var vm = EvaluateVm(new ForLoop(null, new Constant(0), null, new Constant(42)));
        await Assert.That(vm.HasValue).IsFalse();
    }

    [Test]
    public async Task WhileLoop_CountsToFive_ViaLambda() {
        var i = new Variable("i");
        var body = new Block([
            new Assignment(i, new Constant(0)),
            new WhileLoop(new LessThan(new Variable("i"), new Constant(5)),
                new Assignment(i, new Add(new Variable("i"), new Constant(1)))),
            new Variable("i")
        ], [i]);
        var lambda = new Lambda([], body);
        var vm = EvaluateVm(new Invoke(lambda, []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(5);
    }

    [Test]
    public async Task DoWhileLoop_BodyExecutesOnce_VmOnly() {
        var vm = EvaluateVm(new DoWhileLoop(new Constant(42), new Constant(0)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
    }

    [Test]
    public async Task WhileLoop_Break_ExitsEarly_VmOnly() {
        var node = new WhileLoop(new Constant(1),
            new IfStatement(new Constant(1), new BreakStatement()));
        var vm = EvaluateVm(node);
        await Assert.That(vm.HasValue).IsFalse();
    }

    [Test]
    public async Task NestedConditional_Parity() {
        await AssertParityInt(
            new Conditional(new Constant(1),
                new Conditional(new Constant(0), new Constant(10),
                    new Conditional(new Constant(1), new Constant(20), new Constant(30))),
                new Constant(99)), 20);
    }

    [Test]
    public async Task Block_Multi_Parity() {
        var vm = EvaluateVm(new Block([new Constant(1), new Constant(2), new Constant(3)]));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(3);
    }

    [Test]
    public async Task DoubleAdd_Parity() {
        var vm = EvaluateVm(new Add(new Constant(1.5), new Constant(2.5)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(4.0);
    }

    [Test]
    public async Task DoubleSub_Parity() {
        var vm = EvaluateVm(new Subtract(new Constant(3.0), new Constant(1.5)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(1.5);
    }

    [Test]
    public async Task Equal_True_Parity() {
        await AssertParityInt(new Equal(new Constant(1), new Constant(1)), 1);
    }

    [Test]
    public async Task Equal_False_Parity() {
        await AssertParityInt(new Equal(new Constant(1), new Constant(2)), 0);
    }

    [Test]
    public async Task LessThan_True_Parity() {
        await AssertParityInt(new LessThan(new Constant(1), new Constant(2)), 1);
    }

    [Test]
    public async Task GreaterThan_True_Parity() {
        await AssertParityInt(new GreaterThan(new Constant(5), new Constant(3)), 1);
    }

    [Test]
    public async Task And_Parity() {
        await AssertParityInt(new And(new Constant(1), new Constant(1)), 1);
    }

    [Test]
    public async Task Or_Parity() {
        await AssertParityInt(new Or(new Constant(0), new Constant(1)), 1);
    }

    [Test]
    public async Task Not_True_Parity() {
        await AssertParityInt(new Not(new Equal(new Constant(0), new Constant(0))), 0);
    }

    [Test]
    public async Task ArithmeticChain_Parity() {
        await AssertParityInt(
            new Add(new Multiply(new Constant(3), new Constant(4)),
                new Divide(new Constant(10), new Constant(2))), 17);
    }

    [Test]
    public async Task TypeCast_Parity() {
        await AssertParityInt(new TypeCast(new Constant(42), TypeReference.To<int>()), 42);
    }

    [Test]
    public async Task TypeIs_NonNull_Parity() {
        await AssertParityInt(new TypeIs(new Constant("hello"), TypeReference.To<string>()), 1);
    }

    [Test]
    public async Task TypeIs_Null_Parity() {
        await AssertParityInt(new TypeIs(new Constant(null), TypeReference.To<string>()), 0);
    }

    [Test]
    public async Task Coalesce_NullLeft_ReturnsRight_VmOnly() {
        var vm = EvaluateVm(new Coalesce(new Constant(null), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
    }

    [Test]
    public async Task Coalesce_NonNullLeft_ReturnsLeft_VmOnly() {
        var vm = EvaluateVm(new Coalesce(new Constant(7), new Constant(42)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(7);
    }

    [Test]
    public async Task Member_StringLength_Parity() {
        var vm = EvaluateVm(new Member(new Constant("hello"), "Length"));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(5);
    }

    [Test]
    public async Task IndexAccess_ListElement_Parity() {
        var list = new Constant(new List<int> { 10, 20, 30 });
        var vm = EvaluateVm(new IndexAccess(list, new Constant(1)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(20);
    }

    [Test]
    public async Task SuspendNode_Parity() {
        var vm = EvaluateVm(new SuspendNode(new Constant(42), "test"));
        await Assert.That(vm.IsSignal).IsTrue();
        await Assert.That(vm.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Suspend);
    }

    [Test]
    public async Task Evaluate_Repeatedly_SameAst_ProducesIdenticalResult() {
        var node = new Add(new Constant(3), new Constant(4));
        var r1 = EvaluateVm(node);
        var r2 = EvaluateVm(node);
        await Assert.That(r1.HasValue).IsTrue();
        await Assert.That(r2.HasValue).IsTrue();
        await Assert.That(r1.Value).IsEqualTo(7);
        await Assert.That(r2.Value).IsEqualTo(7);
    }

    [Test]
    public async Task UnusedValue_DoesNotLeakToResult() {
        var vm = EvaluateVm(new Block([new Constant(1), new Constant(2), new Constant(3)]));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(3);
    }

    private static byte Op(OpCode op) => (byte)op;

    private static byte[] Int32(int value) =>
        [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF), (byte)((value >> 24) & 0xFF)];

    private static byte[] J(OpCode op, int data) => [Op(op), .. Int32(data)];

    private static InterpreterResult EvaluateVmOnly(Node node,
        Dictionary<string, object?>? initialVariables = null) {
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseStackDepthAnalysis()
            .UseDefiniteAssignmentAnalysis()
            .Build()
            .Analyze(node);
        var program = Lowering.Lower(node, analysis);
        var state = new VmState { Program = program };
        var result = Vm.Execute(state);
        if (!result.HasValue) {
            Console.Error.WriteLine($"Functions ({program.Functions.Count}):");
            foreach (var f in program.Functions)
                Console.Error.WriteLine($"  PC={f.PC} ArgBytes={f.ArgBytes} RetBytes={f.RetBytes} LocalCount={f.LocalCount}");
            Console.Error.WriteLine($"Bytecode: {string.Join(" ", program.Code.Select(b => b.ToString("X2")))}");
            var sp = state.Stack.SP;
            if (sp > 0) Console.Error.WriteLine($"  Top: {state.Stack.AsSpan()[sp - 1]}");
            Console.Error.WriteLine($"Result: Kind={result.Kind} Signal={result.Signal?.Kind}");
        }
        state.Dispose();
        return result;
    }

    // ─── Tier 1: Gap closure tests ─────────────────────────────────

    [Test]
    public async Task NotEqual_True_Parity() {
        await AssertParityInt(new NotEqual(new Constant(1), new Constant(2)), 1);
    }

    [Test]
    public async Task NotEqual_False_Parity() {
        await AssertParityInt(new NotEqual(new Constant(1), new Constant(1)), 0);
    }

    [Test]
    public async Task LessThanOrEqual_True_Parity() {
        await AssertParityInt(new LessThanOrEqual(new Constant(2), new Constant(2)), 1);
    }

    [Test]
    public async Task LessThanOrEqual_False_Parity() {
        await AssertParityInt(new LessThanOrEqual(new Constant(3), new Constant(2)), 0);
    }

    [Test]
    public async Task GreaterThanOrEqual_True_Parity() {
        await AssertParityInt(new GreaterThanOrEqual(new Constant(5), new Constant(5)), 1);
    }

    [Test]
    public async Task GreaterThanOrEqual_False_Parity() {
        await AssertParityInt(new GreaterThanOrEqual(new Constant(2), new Constant(5)), 0);
    }

    [Test]
    public async Task GotoStatement_JumpsForward_Parity() {
        var result = new Variable("result");
        var body = new Block([
            new Assignment(result, new Constant(0)),
            new GotoStatement("skip"),
            new Assignment(result, new Constant(99)),
            new LabelDeclaration("skip", new Constant(0)),
            new Variable("result")
        ], [result]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(0);
    }

    [Test]
    public async Task ContinueStatement_InWhileLoop_Parity() {
        var i = new Variable("i");
        var sum = new Variable("sum");
        var body = new Block([
            new Assignment(i, new Constant(0)),
            new Assignment(sum, new Constant(0)),
            new WhileLoop(
                new LessThan(new Variable("i"), new Constant(5)),
                new Block([
                    new Assignment(i, new Add(new Variable("i"), new Constant(1))),
                    new IfStatement(new Equal(new Variable("i"), new Constant(3)), new ContinueStatement()),
                    new Assignment(sum, new Add(new Variable("sum"), new Constant(1)))
                ])),
            new Variable("sum")
        ], [i, sum]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(4);
    }

    [Test]
    public async Task LabeledBreak_ExitsNamedLoop_Parity() {
        var i = new Variable("i");
        var body = new Block([
            new Assignment(i, new Constant(0)),
            new LabelDeclaration("outer",
                new WhileLoop(new Constant(1),
                    new Block([
                        new Assignment(i, new Add(new Variable("i"), new Constant(1))),
                        new IfStatement(new Equal(new Variable("i"), new Constant(3)),
                            new BreakStatement("outer"))
                    ]))),
            new Variable("i")
        ], [i]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(3);
    }

    [Test]
    public async Task TryCatchFinally_TryBlock_NoThrow_Parity() {
        var vm = EvaluateVm(new TryCatchFinally(
            new Add(new Constant(3), new Constant(4)),
            [new CatchClause(null, "ex", new Constant(99))]));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(7);
    }

    [Test]
    public async Task TryCatchFinally_FinallyBlock_RunsOnThrow_Parity() {
        var flag = new Variable("flag");
        var body = new Block([
            new Assignment(flag, new Constant(0)),
            new TryCatchFinally(
                new ThrowStatement(new Constant(-1)),
                null,
                new Assignment(flag, new Constant(1))),
            new Variable("flag")
        ], [flag]);
        var exResult = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(exResult.IsSignal).IsTrue();
        await Assert.That(exResult.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Throw);
    }

    [Test]
    public async Task ForEachLoop_OverList_IteratesAll_Parity() {
        var x = new Variable("x");
        var body = new Block([
            new Assignment(x, new Constant(0)),
            new ForEachLoop(x, new Constant(new List<int> { 10, 20, 30 }), new Variable("x")),
            new Variable("x")
        ], [x]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(30);
    }

    [Test]
    public async Task UsingStatement_DisposesResource_Parity() {
        var disposed = false;
        var resource = new DisposableResource(() => disposed = true);
        var body = new Block([
            new UsingStatement(new Constant(resource), new Constant(42))
        ]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
        await Assert.That(disposed).IsTrue();
    }

    [Test]
    public async Task ThrowStatement_DirectThrow_Signals_Parity() {
        var vm = EvaluateVm(new ThrowStatement(new Constant(-1)));
        await Assert.That(vm.IsSignal).IsTrue();
        await Assert.That(vm.Signal?.Kind).IsEqualTo(InterpreterSignal.SignalKind.Throw);
    }

    [Test]
    public async Task SwitchStatement_MatchesCorrectCase_Parity() {
        var vm = EvaluateVm(new SwitchStatement(
            new Constant(2),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
                new SwitchCase(new Constant(2), new Constant(20)),
                new SwitchCase(new Constant(3), new Constant(30)),
            ]));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(20);
    }

    [Test]
    public async Task SwitchStatement_DefaultCase_Parity() {
        var vm = EvaluateVm(new SwitchStatement(
            new Constant(99),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
                new SwitchCase(new Constant(2), new Constant(20)),
            ],
            new Constant(99)));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(99);
    }

    [Test]
    public async Task SwitchStatement_NoMatchNoDefault_ReturnsVoid_Parity() {
        var vm = EvaluateVm(new SwitchStatement(
            new Constant(99),
            [
                new SwitchCase(new Constant(1), new Constant(10)),
            ]));
        await Assert.That(vm.HasValue).IsFalse();
    }

    [Test]
    public async Task Lambda_ClosureCapture_ReadsUpvalue_Parity() {
        var outer = new Variable("outer");
        var body = new Block([
            new Assignment(outer, new Constant(42)),
            new Invoke(new Lambda([], new Variable("outer")), [])
        ], [outer]);
        var vm = EvaluateVm(new Invoke(new Lambda([], body), []));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(42);
    }

    [Test]
    public async Task New_ConstructsObject_Parity() {
        var sb = new System.Text.StringBuilder("hello");
        var vm = EvaluateVm(new Member(new Constant(sb), "Length"));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(vm.Value).IsEqualTo(5);
    }

    // ─── Optimizer tests ──────────────────────────────────────────

    private sealed class DisposableResource(Action onDispose) : IDisposable {
        public void Dispose() => onDispose();
    }

#if DEBUG
    [Test]
    public async Task Optimizer_IdentityFold_RemovesPushInt0Add() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 0),
            Op(OpCode.Add),
            .. J(OpCode.PushInt, 42),
        ], []);
        var optimized = Optimizer.Optimize(prog);
        using var state = new VmState { Program = optimized };
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
    }

    [Test]
    public async Task Optimizer_IdentityFold_RemovesPushInt1Mul() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 1),
            Op(OpCode.Mul),
            .. J(OpCode.PushInt, 99),
        ], []);
        var optimized = Optimizer.Optimize(prog);
        using var state = new VmState { Program = optimized };
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(99);
    }

    [Test]
    public async Task Optimizer_ZeroSub_RemovesIdentity() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 7),
            .. J(OpCode.PushInt, 0),
            Op(OpCode.Sub),
        ], []);
        var optimized = Optimizer.Optimize(prog);
        using var state = new VmState { Program = optimized };
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task Optimizer_DupPop_Eliminated() {
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 7),
            Op(OpCode.Dup),
            Op(OpCode.Pop),
            Op(OpCode.Dup),
            Op(OpCode.Pop),
        ], []);
        var optimized = Optimizer.Optimize(prog);
        using var state = new VmState { Program = optimized };
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(7);
    }

    [Test]
    public async Task Optimizer_PreservesSemantics_OnComplexArithmetic() {
        // (10 * 3) * 1 + 0 + 7 = 37, with identity folds and Dup/Pop
        var prog = new Bytecode([
            .. J(OpCode.PushInt, 10),
            .. J(OpCode.PushInt, 3),
            Op(OpCode.Mul),
            .. J(OpCode.PushInt, 1), Op(OpCode.Mul),
            .. J(OpCode.PushInt, 0), Op(OpCode.Add),
            .. J(OpCode.PushInt, 7),
            Op(OpCode.Add),
            Op(OpCode.Dup), Op(OpCode.Pop),
        ], []);
        var optimized = Optimizer.Optimize(prog);
        using var expected = new VmState { Program = prog };
        var eResult = Vm.Execute(expected);
        using var state = new VmState { Program = optimized };
        var result = Vm.Execute(state);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(eResult.Value);
    }

    [Test]
    public async Task Vm_Tracing_ProducesOutput() {
        var sw = new StringWriter();
        sw.WriteLine("=== simple_arith ===");
        try {
            var node = new Add(new Constant(3), new Constant(4));
            var analysis = new AnalyzerBuilder().UseTypeResolver().Build().Analyze(node);
            var program = Lowering.Lower(node, analysis);
            using var state = new VmState { Program = program, Trace = sw, NodeDescriptions = VmState.BuildNodeDescriptions(node) };
            var result = Vm.Execute(state);
            await Assert.That(result.HasValue).IsTrue();
            await Assert.That(result.Value).IsEqualTo(7);
            string trace = sw.ToString();
            await Assert.That(trace.Contains("PC:")).IsTrue();
        }
        catch (Exception ex) {
            sw.WriteLine($"--- Error: {ex.Message} ---");
            Console.WriteLine(sw.ToString());
            throw;
        }
    }
#endif
}