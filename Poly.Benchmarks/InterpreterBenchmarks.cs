using System.Linq.Expressions;

using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.VirtualMachine;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Benchmarks;

[MemoryDiagnoser]
public class InterpreterBenchmarks {
    private Parameter? _polyX;
    private Node? _poly, _polyParam;
    private AnalysisResult? _polyAnalysis, _polyParamAnalysis;
    private Func<object?>? _linqPolyJit, _linqPolyInterp;
    private Bytecode? _vmPolyProgram;
    private Func<int, int>? _linqPolyJitParam, _linqPolyInterpParam;

    private Node? _deepSum1000, _deepSum10000, _deepSum100000;
    private Func<object?>? _linqDeepSumJit, _linqDeepSumInterp;
    private Bytecode? _vmDeepSum1000, _vmDeepSum10000, _vmDeepSum100000;
    private VmState? _vmState;

    [GlobalSetup]
    public void Setup() {
        _poly = new Add(
            new Add(
                new Add(
                    new Multiply(new Constant(3),
                        new Multiply(new Constant(5), new Multiply(new Constant(5), new Constant(5)))),
                    new Multiply(new Constant(2), new Multiply(new Constant(5), new Constant(5)))
                ),
                new Constant(5)
            ),
            new Constant(5)
        );

        _polyX = new Parameter("x", new TypeReference(typeof(int).FullName!));
        _polyParam = new Add(
            new Add(
                new Add(
                    new Multiply(new Constant(3),
                        new Multiply(_polyX, new Multiply(_polyX, _polyX))),
                    new Multiply(new Constant(2), new Multiply(_polyX, _polyX))
                ),
                _polyX
            ),
            new Constant(5)
        );

        _polyAnalysis = Analyze(_poly);
        _polyParamAnalysis = Analyze(_polyParam);

        _vmPolyProgram = Lowering.Lower(_poly, _polyAnalysis);
        _linqPolyJit = CompileToFunc(_poly, _polyAnalysis, preferInterpretation: false);
        _linqPolyInterp = CompileToFunc(_poly, _polyAnalysis, preferInterpretation: true);

        var gen = new LinqExpressionGenerator(_polyParamAnalysis);
        _linqPolyJitParam = gen.CompileAsDelegate<Func<int, int>>(_polyParam!, _polyX!);
        _linqPolyInterpParam = (Func<int, int>)gen
            .CompileAsLambda(_polyParam!, _polyX!)
            .Compile(preferInterpretation: true);

        _deepSum1000 = BuildDeepSum(1000);
        var deepAnalysis = Analyze(_deepSum1000);
        _linqDeepSumJit = CompileToFunc(_deepSum1000, deepAnalysis, preferInterpretation: false);
        _linqDeepSumInterp = CompileToFunc(_deepSum1000, deepAnalysis, preferInterpretation: true);
        _vmDeepSum1000 = Lowering.Lower(_deepSum1000, deepAnalysis);

        _deepSum10000 = BuildDeepSum(10000);
        _vmDeepSum10000 = Lowering.Lower(_deepSum10000, Analyze(_deepSum10000));

        _deepSum100000 = BuildDeepSum(100000);
        _vmDeepSum100000 = Lowering.Lower(_deepSum100000, Analyze(_deepSum100000));

        _vmState = new VmState();
    }

    [GlobalCleanup]
    public void Cleanup() {
        _vmState?.Dispose();
    }

    private static Node BuildDeepSum(int n) {
        // Balanced binary tree (depth log2(N)) to avoid analysis stack overflow
        int[] values = new int[n];
        for (int i = 0; i < n; i++) values[i] = i + 1;
        return BuildBalanced(values, 0, n - 1);
    }

    private static Node BuildBalanced(int[] values, int start, int end) {
        if (start == end) return new Constant(values[start]);
        int mid = (start + end) / 2;
        return new Add(BuildBalanced(values, start, mid), BuildBalanced(values, mid + 1, end));
    }

    [Benchmark(Baseline = true)]
    public int Baseline_Poly() => 3 * 5 * 5 * 5 + 2 * 5 * 5 + 5 + 5;

    [Benchmark]
    public object? LinqJit_Poly() => _linqPolyJit!();

    [Benchmark]
    public object? LinqInterp_Poly() => _linqPolyInterp!();

    [Benchmark]
    public object? Vm_Poly() {
        _vmState!.Program = _vmPolyProgram;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    [Benchmark]
    public int Baseline_PolyParam() {
        int x = 5;
        return 3 * x * x * x + 2 * x * x + x + 5;
    }

    [Benchmark]
    public int LinqJit_PolyParam() => _linqPolyJitParam!(5);

    [Benchmark]
    public int LinqInterp_PolyParam() => _linqPolyInterpParam!(5);

    // ── Deep sum(1..N) benchmarks ──

    [Benchmark]
    public long Baseline_Sum1000() {
        long sum = 0;
        for (int i = 1; i <= 1000; i++) sum += i;
        return sum;
    }

    [Benchmark]
    public object? LinqJit_Sum1000() => _linqDeepSumJit!();

    [Benchmark]
    public object? LinqInterp_Sum1000() => _linqDeepSumInterp!();

    [Benchmark]
    public object? Vm_Sum1000() {
        _vmState!.Program = _vmDeepSum1000;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    [Benchmark]
    public long Baseline_Sum10000() {
        long sum = 0;
        for (int i = 1; i <= 10000; i++) sum += i;
        return sum;
    }

    [Benchmark]
    public object? Vm_Sum10000() {
        _vmState!.Program = _vmDeepSum10000;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    [Benchmark]
    public long Baseline_Sum100000() {
        long sum = 0;
        for (int i = 1; i <= 100000; i++) sum += i;
        return sum;
    }

    [Benchmark]
    public object? Vm_Sum100000() {
        _vmState!.Program = _vmDeepSum100000;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    private static AnalysisResult Analyze(Node node) {
        return new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseConstantFolding()
            .UseSideEffectAnalysis()
            .UseThisReferenceContext()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(node);
    }

    private static Func<object?> CompileToFunc(Node node, AnalysisResult analysis, bool preferInterpretation) {
        var expr = new LinqExpressionGenerator(analysis).Compile(node).Expression;
        var boxed = expr.Type.IsValueType
            ? Expression.Convert(expr, typeof(object))
            : expr;
        return Expression.Lambda<Func<object?>>(boxed).Compile(preferInterpretation);
    }
}