using Poly.Interpretation;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class ClassicProgramTests {
    private static InterpreterResult Run(Node root) {
        var analysis = new AnalyzerBuilder().UseAllAnalyzers().Build().Analyze(root);
        var program = Lowering.Lower(root, analysis);
        using var state = new VmState { Program = program };
        return Vm.Execute(state);
    }

    private static async Task AssertInt(Node root, int expected) {
        var result = Run(root);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expected);
    }

    [Test] public async Task Fib_0_Returns0() => await FibTest(0, 0);
    [Test] public async Task Fib_1_Returns1() => await FibTest(1, 1);
    [Test] public async Task Fib_10_Returns55() => await FibTest(10, 55);
    [Test] public async Task Fact_0_Returns1() => await FactTest(0, 1);
    [Test] public async Task Fact_5_Returns120() => await FactTest(5, 120);
    [Test] public async Task Gcd_12_8_Returns4() => await GcdTest(12, 8, 4);
    [Test] public async Task Gcd_1071_462_Returns21() => await GcdTest(1071, 462, 21);
    [Test] public async Task SumOfSquares_5_Returns55() => await SumSqTest(5, 55);

    private static async Task FibTest(int n, int expected) {
        var p = new Parameter("n");
        Variable i = new("i"), a = new("a"), b = new("b"), t = new("t");
        var body = new Block([
            new Assignment(a, new Constant(0)), new Assignment(b, new Constant(1)),
            new ForLoop(new Assignment(i, new Constant(0)), new LessThan(new Variable("i"), new Variable("n")),
                new Assignment(i, new Add(new Variable("i"), new Constant(1))),
                new Block([new Assignment(t, new Add(new Variable("a"), new Variable("b"))),
                    new Assignment(a, new Variable("b")), new Assignment(b, new Variable("t"))])),
            new Variable("a")], [i, a, b, t]);
        await AssertInt(new Invoke(new Lambda([p], body), [new Constant(n)]), expected);
    }

    private static async Task FactTest(int n, int expected) {
        var p = new Parameter("n");
        Variable i = new("i"), r = new("r");
        var body = new Block([
            new Assignment(r, new Constant(1)),
            new ForLoop(new Assignment(i, new Constant(1)), new LessThanOrEqual(new Variable("i"), new Variable("n")),
                new Assignment(i, new Add(new Variable("i"), new Constant(1))),
                new Assignment(r, new Multiply(new Variable("r"), new Variable("i")))),
            new Variable("r")], [i, r]);
        await AssertInt(new Invoke(new Lambda([p], body), [new Constant(n)]), expected);
    }

    private static async Task GcdTest(int va, int vb, int expected) {
        var pa = new Parameter("a"); var pb = new Parameter("b"); Variable t = new("t");
        var body = new Block([
            new WhileLoop(new NotEqual(new Variable("b"), new Constant(0)),
                new Block([new Assignment(t, new Variable("b")),
                    new Assignment(new Variable("b"), new Modulo(new Variable("a"), new Variable("b"))),
                    new Assignment(new Variable("a"), new Variable("t"))])),
            new Variable("a")], [t]);
        await AssertInt(new Invoke(new Lambda([pa, pb], body), [new Constant(va), new Constant(vb)]), expected);
    }

    private static async Task SumSqTest(int n, int expected) {
        var p = new Parameter("n");
        Variable i = new("i"), s = new("s");
        var body = new Block([
            new Assignment(s, new Constant(0)),
            new ForLoop(new Assignment(i, new Constant(1)), new LessThanOrEqual(new Variable("i"), new Variable("n")),
                new Assignment(i, new Add(new Variable("i"), new Constant(1))),
                new Assignment(s, new Add(new Variable("s"), new Multiply(new Variable("i"), new Variable("i"))))),
            new Variable("s")], [i, s]);
        await AssertInt(new Invoke(new Lambda([p], body), [new Constant(n)]), expected);
    }
}