using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

/// <summary>Real-world algorithmic tests: AST → Lowering → µops → execute.
/// Exercises the full pipeline on nontrivial programs (loops, recursion,
/// clr calls, etc.).</summary>
public class UopRealWorldTests {
    private static Bytecode LowerWith(Node node) {
        var builder = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .UseVariableScopeValidator();
        var analysis = builder.Build().Analyze(node);
        return Lowering.Lower(node, analysis);
    }

    private static InterpreterResult Execute(Node node) {
        var prog = LowerWith(node);
        using var state = new VmState { Program = prog };
        return Vm.Execute(state);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Fibonacci (iterative)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Fib_0() => await Assert.That(Fib(0)).IsEqualTo(0L);

    [Test]
    public async Task Fib_1() => await Assert.That(Fib(1)).IsEqualTo(1L);

    [Test]
    public async Task Fib_10() => await Assert.That(Fib(10)).IsEqualTo(55L);

    [Test]
    public async Task Fib_20() => await Assert.That(Fib(20)).IsEqualTo(6765L);

    private static long Fib(int n) {
        // while (i < n) { next = a + b; a = b; b = next; i++; }
        var a = new Variable("a");
        var b = new Variable("b");
        var i = new Variable("i");
        var next = new Variable("next");
        var body = new Block(
            [new Assignment(a, new Constant(0L)),
             new Assignment(b, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(
                 new LessThan(i, new Constant(n)),
                 new Block([
                     new Assignment(next, new Add(a, b)),
                     new Assignment(a, b),
                     new Assignment(b, next),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             a],
            [a, b, i, next]);
        var result = Execute(new Invoke(new Lambda([], body)));
        return (long)result!.Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Factorial (iterative)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Fact_0() => await Assert.That(Fact(0)).IsEqualTo(1L);

    [Test]
    public async Task Fact_1() => await Assert.That(Fact(1)).IsEqualTo(1L);

    [Test]
    public async Task Fact_5() => await Assert.That(Fact(5)).IsEqualTo(120L);

    [Test]
    public async Task Fact_10() => await Assert.That(Fact(10)).IsEqualTo(3628800L);

    private static long Fact(int n) {
        var result = new Variable("result");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(result, new Constant(1L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(
                 new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(result, new Multiply(result, i)),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             result],
            [result, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Sum of squares: Σ i² for i = 1..n
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task SumSquares_1() => await Assert.That(SumSquares(1)).IsEqualTo(1L);

    [Test]
    public async Task SumSquares_5() => await Assert.That(SumSquares(5)).IsEqualTo(55L);

    [Test]
    public async Task SumSquares_10() => await Assert.That(SumSquares(10)).IsEqualTo(385L);

    private static long SumSquares(int n) {
        var sum = new Variable("sum");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(sum, new Constant(0L)),
             new Assignment(i, new Constant(1L)),
             new WhileLoop(
                 new LessThanOrEqual(i, new Constant(n)),
                 new Block([
                     new Assignment(sum, new Add(sum, new Multiply(i, i))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             sum],
            [sum, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Greatest Common Divisor (Euclidean algorithm)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Gcd_12_8() => await Assert.That(Gcd(12, 8)).IsEqualTo(4L);

    [Test]
    public async Task Gcd_54_24() => await Assert.That(Gcd(54, 24)).IsEqualTo(6L);

    [Test]
    public async Task Gcd_101_10() => await Assert.That(Gcd(101, 10)).IsEqualTo(1L);

    [Test]
    public async Task Gcd_0_5() => await Assert.That(Gcd(0, 5)).IsEqualTo(5L);

    private static long Gcd(int a, int b) {
        var x = new Variable("x");
        var y = new Variable("y");
        var tmp = new Variable("tmp");
        // while (y != 0) { tmp = x % y; x = y; y = tmp; }
        var body = new Block(
            [new Assignment(x, new Constant((long)a)),
             new Assignment(y, new Constant((long)b)),
             new WhileLoop(
                 new NotEqual(y, new Constant(0L)),
                 new Block([
                     new Assignment(tmp, new Modulo(x, y)),
                     new Assignment(x, y),
                     new Assignment(y, tmp)
                 ])),
             x],
            [x, y, tmp]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR-powered: Math.Pow via repeated multiplication
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Power_2_0() => await Assert.That(Power(2, 0)).IsEqualTo(1L);

    [Test]
    public async Task Power_2_10() => await Assert.That(Power(2, 10)).IsEqualTo(1024L);

    [Test]
    public async Task Power_3_5() => await Assert.That(Power(3, 5)).IsEqualTo(243L);

    private static long Power(int b, int e) {
        var result = new Variable("result");
        var i = new Variable("i");
        var body = new Block(
            [new Assignment(result, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(
                 new LessThan(i, new Constant(e)),
                 new Block([
                     new Assignment(result, new Multiply(result, new Constant((long)b))),
                     new Assignment(i, new Add(i, new Constant(1)))
                 ])),
             result],
            [result, i]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR call: string concatenation via string.Concat
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task StringConcat_HelloWorld() {
        var concat = new Add(new Constant("Hello, "), new Constant("World!"));
        var result = Execute(concat);
        await Assert.That(result.Value).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task StringConcat_Multiple() {
        // "a" + "b" + "c" = "abc"
        var node = new Add(new Add(new Constant("a"), new Constant("b")), new Constant("c"));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo("abc");
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLR call chain: Math.Max on a range
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task ClrMaxChain_50() {
        var maxMethod = new Member(
            new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node chain = new Constant(1);
        for (int i = 2; i <= 50; i++)
            chain = new Invoke(maxMethod, chain, new Constant(i));
        var result = Execute(chain);
        await Assert.That(result.Value).IsEqualTo(50L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deep balanced sum: synthetic stress for lowering + dispatch
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task DeepSum_5000() {
        var node = BuildDeepSum(5000);
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(12502500L);
    }

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

    // ═══════════════════════════════════════════════════════════════
    //  Count digits: while loop with division
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task CountDigits_0() => await Assert.That(CountDigits(0)).IsEqualTo(1L);

    [Test]
    public async Task CountDigits_12345() => await Assert.That(CountDigits(12345)).IsEqualTo(5L);

    [Test]
    public async Task CountDigits_1000000() => await Assert.That(CountDigits(1000000)).IsEqualTo(7L);

    private static long CountDigits(int n) {
        var num = new Variable("num");
        var count = new Variable("count");
        var body = new Block(
            [new Assignment(num, new Constant((long)n)),
             new Assignment(count, new Constant(0L)),
             new WhileLoop(
                 new GreaterThan(num, new Constant(0L)),
                 new Block([
                     new Assignment(num, new Divide(num, new Constant(10L))),
                     new Assignment(count, new Add(count, new Constant(1L)))
                 ])),
             new Conditional(
                 new Equal(count, new Constant(0L)),
                 new Constant(1L),
                 count)],
            [num, count]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Reverse a number (arithmetic)
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Reverse_123() => await Assert.That(Reverse(123)).IsEqualTo(321L);

    [Test]
    public async Task Reverse_100() => await Assert.That(Reverse(100)).IsEqualTo(1L);

    [Test]
    public async Task Reverse_987654321() => await Assert.That(Reverse(987654321)).IsEqualTo(123456789L);

    private static long Reverse(int n) {
        var num = new Variable("num");
        var rev = new Variable("rev");
        var body = new Block(
            [new Assignment(num, new Constant((long)n)),
             new Assignment(rev, new Constant(0L)),
             new WhileLoop(
                 new GreaterThan(num, new Constant(0L)),
                 new Block([
                     new Assignment(rev, new Add(
                         new Multiply(rev, new Constant(10L)),
                         new Modulo(num, new Constant(10L)))),
                     new Assignment(num, new Divide(num, new Constant(10L)))
                 ])),
             rev],
            [num, rev]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Sieve-style: is prime test
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task IsPrime_2() => await Assert.That(IsPrime(2)).IsEqualTo(1L);

    [Test]
    public async Task IsPrime_7() => await Assert.That(IsPrime(7)).IsEqualTo(1L);

    [Test]
    public async Task IsPrime_8() => await Assert.That(IsPrime(8)).IsEqualTo(0L);

    [Test]
    public async Task IsPrime_97() => await Assert.That(IsPrime(97)).IsEqualTo(1L);

    [Test]
    public async Task IsPrime_100() => await Assert.That(IsPrime(100)).IsEqualTo(0L);

    private static long IsPrime(int n) {
        var num = new Variable("num");
        var i = new Variable("i");
        var prime = new Variable("prime");
        var body = new Block(
            [new Assignment(num, new Constant((long)n)),
             new Assignment(prime, new Constant(1L)),
             new Assignment(i, new Constant(2L)),
             // if (n < 2) { prime = 0; } else { while (i * i <= n) { ... } }
             new IfStatement(
                 new LessThan(num, new Constant(2L)),
                 new Block([new Assignment(prime, new Constant(0L))]),
                 new Block([
                     new WhileLoop(
                         new LessThanOrEqual(new Multiply(i, i), num),
                         new Block([
                             new IfStatement(
                                 new Equal(new Modulo(num, i), new Constant(0L)),
                                 new Block([
                                     new Assignment(prime, new Constant(0L)),
                                     new Assignment(i, new Add(new Constant(999999L), i))  // force loop exit
                                 ])),
                             new Assignment(i, new Add(i, new Constant(1L)))
                         ]))
                 ])),
             prime],
            [num, i, prime]);
        return (long)Execute(new Invoke(new Lambda([], body))).Value!;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Triangular numbers: T(n) = n * (n + 1) / 2
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task Triangular_10() {
        // T(10) = 55 via formula (not loop)
        var node = new Divide(
            new Multiply(new Constant(10L), new Constant(11L)),
            new Constant(2L));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(55L);
    }

    [Test]
    public async Task Triangular_100() {
        var node = new Divide(
            new Multiply(new Constant(100L), new Constant(101L)),
            new Constant(2L));
        var result = Execute(node);
        await Assert.That(result.Value).IsEqualTo(5050L);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Multiple lambdas: mapping a binary function over arguments
    // ═══════════════════════════════════════════════════════════════

    [Test]
    public async Task TwoLambdas_Independent() {
        // double(x) = x * 2 and triple(x) = x * 3 — each called once
        var doubleProg = LowerWith(new Invoke(
            new Lambda([new Parameter("x", TypeReference.To<int>())],
                new Multiply(new Variable("x"), new Constant(2L))),
            new Constant(7)));
        using var s1 = new VmState { Program = doubleProg };
        await Assert.That(Vm.Execute(s1).Value).IsEqualTo(14L);

        var tripleProg = LowerWith(new Invoke(
            new Lambda([new Parameter("x", TypeReference.To<int>())],
                new Multiply(new Variable("x"), new Constant(3L))),
            new Constant(9)));
        using var s2 = new VmState { Program = tripleProg };
        await Assert.That(Vm.Execute(s2).Value).IsEqualTo(27L);
    }
}