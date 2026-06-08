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
    public async Task TypeIs_NonNull_ReturnsTrue_VmOnly() {
        var vm = EvaluateVm(new TypeIs(new Constant("text"), TypeReference.To<int>()));
        await Assert.That(vm.HasValue).IsTrue();
        await Assert.That(Normalize(vm.Value)).IsEqualTo(1);
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

    private static InterpreterResult EvaluateVmOnly(Node node,
        Dictionary<string, object?>? initialVariables = null) {
        var analysis = new AnalyzerBuilder()
            .UseTypeResolver()
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

#if DEBUG
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