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

    private Node? _clrChain100, _clrChain1000;
    private Func<object?>? _linqClrChainJit, _linqClrChainInterp;
    private Bytecode? _vmClrChain100, _vmClrChain1000;

    private Node? _loopSum1000, _loopSum10000;
    private Bytecode? _vmLoopSum1000, _vmLoopSum10000;
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

        _clrChain100 = BuildClrCallChain(100);
        var chainAnalysis = Analyze(_clrChain100);
        _linqClrChainJit = CompileToFunc(_clrChain100, chainAnalysis, preferInterpretation: false);
        _linqClrChainInterp = CompileToFunc(_clrChain100, chainAnalysis, preferInterpretation: true);
        _vmClrChain100 = Lowering.Lower(_clrChain100, chainAnalysis);

        _clrChain1000 = BuildClrCallChain(1000);
        _vmClrChain1000 = Lowering.Lower(_clrChain1000, Analyze(_clrChain1000));

        _loopSum1000 = BuildLoopSum(1000);
        _vmLoopSum1000 = Lowering.Lower(_loopSum1000, Analyze(_loopSum1000));

        _loopSum10000 = BuildLoopSum(10000);
        _vmLoopSum10000 = Lowering.Lower(_loopSum10000, Analyze(_loopSum10000));

        _vmState = new VmState();
    }

    [GlobalCleanup]
    public void Cleanup() {
        _vmState?.Dispose();
    }

    private static Node BuildClrCallChain(int n) {
        var maxMethod = new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max));
        Node result = new Constant(1);
        for (int i = 2; i <= n; i++)
            result = new Invoke(maxMethod, result, new Constant(i));
        return result;
    }

    private static Node BuildLoopSum(int n) {
        var sumVar = new Variable("sum");
        var iVar = new Variable("i");
        var body = new Block([
            new Assignment(sumVar, new Constant(0)),
            new Assignment(iVar, new Constant(1)),
            new WhileLoop(
                new LessThanOrEqual(iVar, new Constant(n)),
                new Block([
                    new Assignment(sumVar, new Add(sumVar, iVar)),
                    new Assignment(iVar, new Add(iVar, new Constant(1)))
                ])
            ),
            sumVar
        ]);
        var lambda = new Lambda([], body);
        return new Invoke(lambda);
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

    // ── CLR call chain (Math.Max) benchmarks ──

    [Benchmark]
    public long Baseline_ClrChain100() {
        long r = 1;
        for (int i = 2; i <= 100; i++) r = Math.Max(r, i);
        return r;
    }

    [Benchmark]
    public object? LinqJit_ClrChain100() => _linqClrChainJit!();

    [Benchmark]
    public object? LinqInterp_ClrChain100() => _linqClrChainInterp!();

    [Benchmark]
    public object? Vm_ClrChain100() {
        _vmState!.Program = _vmClrChain100;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    [Benchmark]
    public long Baseline_ClrChain1000() {
        long r = 1;
        for (int i = 2; i <= 1000; i++) r = Math.Max(r, i);
        return r;
    }

    [Benchmark]
    public object? Vm_ClrChain1000() {
        _vmState!.Program = _vmClrChain1000;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    // ── Loop sum benchmarks (lambda + while loop) ──

    [Benchmark]
    public long Baseline_LoopSum1000() {
        long sum = 0;
        for (int i = 1; i <= 1000; i++) sum += i;
        return sum;
    }

    [Benchmark]
    public object? Vm_LoopSum1000() {
        _vmState!.Program = _vmLoopSum1000;
        _vmState.Reset();
        return Vm.Execute(_vmState).Value;
    }

    [Benchmark]
    public long Baseline_LoopSum10000() {
        long sum = 0;
        for (int i = 1; i <= 10000; i++) sum += i;
        return sum;
    }

    [Benchmark]
    public object? Vm_LoopSum10000() {
        _vmState!.Program = _vmLoopSum10000;
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
            .UseVariableScopeValidator()
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