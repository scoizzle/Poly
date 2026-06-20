using System.IO;

using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

/// <summary>Profiles every real-world benchmark and writes the report
/// to <c>/tmp/poly_{name}.txt</c> for analysis.</summary>
public class InstructionProfilerTests {
    private static LoweringResult Lower(Node node) {
        var result = new AnalyzerBuilder()
            .UseTypeAndMemberResolver().UseConstantFolding().UseSideEffectAnalysis()
            .UseThisReferenceContext().UseControlFlowAnalysis().UseVariableScopeValidator()
            .Build().Analyze(node);
        return Lowering.Lower(node, result);
    }

    private static void ProfileToFile(string name, Node node) {
        var lowerResult = Lower(node);
        var program = ProgramCompiler.Compile(lowerResult, mode: CompilationMode.Profiling);
        var state = new VmState(program);
        program.Delegate(state);

        if (state.InstructionCounters == null)
            return;

        // Dump profile info
        var path = $"/tmp/poly_{name}.txt";
        using var w = new StreamWriter(path);
        w.WriteLine($"=== {name} PROFILE ===");
        long total = 0;
        for (int i = 0; i < state.InstructionCounters.Length; i++) {
            if (state.InstructionCounters[i] > 0)
                w.WriteLine($"  [{i,4}] {state.InstructionCounters[i],15:N0}");
            total += state.InstructionCounters[i];
        }
        w.WriteLine($"\nTotal µops: {total:N0}");
    }

    [Test] public async Task Profile_LoopSum_1000() => ProfileToFile("loop_sum_1000", BuildLoopSum(1000L));
    [Test] public async Task Profile_LoopSum_10000() => ProfileToFile("loop_sum_10000", BuildLoopSum(10000L));
    [Test] public async Task Profile_Fib_20() => ProfileToFile("fib_20", BuildFib(20L));
    [Test] public async Task Profile_Fact_10() => ProfileToFile("fact_10", BuildFact(10L));
    [Test] public async Task Profile_Gcd() => ProfileToFile("gcd", BuildGcd(123456L, 7890L));
    [Test] public async Task Profile_SumSquares_10() => ProfileToFile("sum_squares_10", BuildSumSquares(10L));
    [Test] public async Task Profile_CountPrimes_1000() => ProfileToFile("count_primes_1000", BuildCountPrimes(1000L));
    [Test] public async Task Profile_Collatz_1000() => ProfileToFile("collatz_1000", BuildCollatz(1000L));

    private static Node BuildLoopSum(long n) {
        var sum = new Variable("sum"); var i = new Variable("i");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(sum, new Constant(0L)), new Assignment(i, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(n)),
                 new Block([new Assignment(sum, new Add(sum, i)),
                            new Assignment(i, new Add(i, new Constant(1L)))])),
             sum], [sum, i])));
    }

    private static Node BuildFib(long n) {
        var a = new Variable("a"); var b = new Variable("b");
        var i = new Variable("i"); var next = new Variable("next");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(a, new Constant(0L)), new Assignment(b, new Constant(1L)),
             new Assignment(i, new Constant(0L)),
             new WhileLoop(new LessThan(i, new Constant(n)),
                 new Block([new Assignment(next, new Add(a, b)),
                            new Assignment(a, b), new Assignment(b, next),
                            new Assignment(i, new Add(i, new Constant(1L)))])),
             a], [a, b, i, next])));
    }

    private static Node BuildFact(long n) {
        var r = new Variable("r"); var i = new Variable("i");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(r, new Constant(1L)), new Assignment(i, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(n)),
                 new Block([new Assignment(r, new Multiply(r, i)),
                            new Assignment(i, new Add(i, new Constant(1L)))])),
             r], [r, i])));
    }

    private static Node BuildGcd(long a, long b) {
        var x = new Variable("x"); var y = new Variable("y"); var tmp = new Variable("tmp");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(x, new Constant(a)), new Assignment(y, new Constant(b)),
             new WhileLoop(new NotEqual(y, new Constant(0L)),
                 new Block([new Assignment(tmp, new Modulo(x, y)),
                            new Assignment(x, y), new Assignment(y, tmp)])),
             x], [x, y, tmp])));
    }

    private static Node BuildSumSquares(long n) {
        var s = new Variable("s"); var i = new Variable("i");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(s, new Constant(0L)), new Assignment(i, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(i, new Constant(n)),
                 new Block([new Assignment(s, new Add(s, new Multiply(i, i))),
                            new Assignment(i, new Add(i, new Constant(1L)))])),
             s], [s, i])));
    }

    private static Node BuildCountPrimes(long limit) {
        var n = new Variable("n"); var i = new Variable("i");
        var count = new Variable("count"); var isPrime = new Variable("isPrime");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(count, new Constant(0L)), new Assignment(n, new Constant(2L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([new Assignment(isPrime, new Constant(1L)),
                     new Assignment(i, new Constant(2L)),
                     new WhileLoop(new And(new LessThanOrEqual(new Multiply(i, i), n),
                         new Equal(isPrime, new Constant(1L))),
                         new Block([new Assignment(isPrime, new Conditional(
                             new Equal(new Modulo(n, i), new Constant(0L)),
                             new Constant(0L), isPrime)),
                             new Assignment(i, new Add(i, new Constant(1L)))])),
                     new Assignment(count, new Add(count,
                         new Conditional(new Equal(isPrime, new Constant(1L)),
                             new Constant(1L), new Constant(0L)))),
                     new Assignment(n, new Add(n, new Constant(1L)))])),
             count], [n, i, count, isPrime])));
    }

    private static Node BuildCollatz(long limit) {
        var n = new Variable("n"); var i = new Variable("i");
        var len = new Variable("len"); var maxLen = new Variable("maxLen");
        var bestN = new Variable("bestN");
        return new Invoke(new Lambda([], new Block(
            [new Assignment(maxLen, new Constant(0L)), new Assignment(bestN, new Constant(0L)),
             new Assignment(n, new Constant(1L)),
             new WhileLoop(new LessThanOrEqual(n, new Constant(limit)),
                 new Block([new Assignment(len, new Constant(0L)), new Assignment(i, n),
                     new WhileLoop(new NotEqual(i, new Constant(1L)),
                         new Block([new Assignment(i, new Conditional(
                             new Equal(new Modulo(i, new Constant(2L)), new Constant(0L)),
                             new ShiftRight(i, new Constant(1)),
                             new Add(new Multiply(i, new Constant(3L)), new Constant(1L)))),
                             new Assignment(len, new Add(len, new Constant(1L)))])),
                     new IfStatement(new GreaterThan(len, maxLen),
                         new Block([new Assignment(maxLen, len), new Assignment(bestN, n)])),
                     new Assignment(n, new Add(n, new Constant(1L)))])),
             new BitwiseOr(new ShiftLeft(bestN, new Constant(32L)), maxLen)],
            [n, i, len, maxLen, bestN])));
    }
}