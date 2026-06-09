using System.Linq.Expressions;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

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
    // ─── Polynomial: 3*5*5*5 + 2*5*5 + 5 + 5 = 435 ───

    private Node? _poly;
    private Bytecode? _progPoly;
    private Func<object?>? _linqPolyJit, _linqPolyInterp;

    // ─── Parameterized: 3*x*x*x + 2*x*x + x + 5, x=5 ───

    private Parameter? _polyX;
    private Node? _polyParam;
    private Func<int, int>? _linqPolyJitParam, _linqPolyInterpParam;

    // ─── Parameterized via VM lambda ───

    private Node? _polyParamLambda;
    private Bytecode? _progPolyParam;

    // ─── CLR: Math.Max(Math.Min(100, 200), 50) = 100 ───

    private Node? _clrSimple;
    private Bytecode? _progClrSimple;

    // ─── CLR chain: Math.Pow(Max(Min(100,200),50), 2) = 10000 ───

    private Node? _clrChain;
    private Bytecode? _progClrChain;

    // ─── Nested add: 1+...+100 = 5050 ───

    private Node? _nested100;
    private Bytecode? _progNested;

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

        _clrSimple = new Invoke(
            new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max)),
            new Invoke(
                new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Min)),
                new Constant(100), new Constant(200)
            ),
            new Constant(50)
        );

        _clrChain = new Invoke(
            new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Pow)),
            _clrSimple, new Constant(2)
        );

        _nested100 = NestedAdd(100);

        // Parameterized VM: wrap in a lambda so the VM can pass the argument
        _polyParamLambda = new Invoke(new Lambda([_polyX!], _polyParam!), new Constant(5));

        // Pre-lower VM programs
        _progPoly = Lower(_poly);
        _progPolyParam = Lower(_polyParamLambda);
        _progClrChain = Lower(_clrChain);
        _progClrSimple = Lower(_clrSimple);
        _progNested = Lower(_nested100);

        // Pre-compile LINQ delegates
        _linqPolyJit = CompileToFunc(_poly, preferInterpretation: false);
        _linqPolyInterp = CompileToFunc(_poly, preferInterpretation: true);

        var paramAnalysis = Analyze(_polyParam!);
        var gen = new LinqExpressionGenerator(paramAnalysis);
        _linqPolyJitParam = gen.CompileAsDelegate<Func<int, int>>(_polyParam!, _polyX!);
        _linqPolyInterpParam = (Func<int, int>)gen
            .CompileAsLambda(_polyParam!, _polyX!)
            .Compile(preferInterpretation: true);
    }

    // ═══════════════════════════════════════════════════════════
    // 1. Constant polynomial: compute 435
    // ═══════════════════════════════════════════════════════════

    [Benchmark(Baseline = true)]
    public int Baseline_Poly() => 3 * 5 * 5 * 5 + 2 * 5 * 5 + 5 + 5;

    [Benchmark]
    public object? Vm_Poly() {
        using var state = new VmState { Program = _progPoly };
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? LinqJit_Poly() => _linqPolyJit!();

    [Benchmark]
    public object? LinqInterp_Poly() => _linqPolyInterp!();

    // ═══════════════════════════════════════════════════════════
    // 2. Parameterized polynomial: compute 3*5^3 + 2*5^2 + 5 + 5 = 435
    // ═══════════════════════════════════════════════════════════

    [Benchmark]
    public int Baseline_PolyParam() {
        int x = 5;
        return 3 * x * x * x + 2 * x * x + x + 5;
    }

    [Benchmark]
    public object? Vm_PolyParam() {
        using var state = new VmState { Program = _progPolyParam };
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public int LinqJit_PolyParam() => _linqPolyJitParam!(5);

    [Benchmark]
    public int LinqInterp_PolyParam() => _linqPolyInterpParam!(5);

    // ═══════════════════════════════════════════════════════════
    // 3. CLR simple: Math.Max(Math.Min(100, 200), 50) = 100
    // ═══════════════════════════════════════════════════════════

    [Benchmark]
    public int Baseline_ClrSimple() => Math.Max(Math.Min(100, 200), 50);

    [Benchmark]
    public object? Vm_ClrSimple() {
        using var state = new VmState { Program = _progClrSimple };
        return Vm.Execute(state).Value;
    }

    // ═══════════════════════════════════════════════════════════
    // 4. CLR chain: Math.Pow(max(min(100,200),50), 2) = 10000
    // ═══════════════════════════════════════════════════════════

    [Benchmark]
    public double Baseline_ClrChain() => Math.Pow(Math.Max(Math.Min(100, 200), 50), 2);

    [Benchmark]
    public object? Vm_ClrChain() {
        using var state = new VmState { Program = _progClrChain };
        return Vm.Execute(state).Value;
    }

    // ═══════════════════════════════════════════════════════════
    // 5. Nested add: 1+2+...+100 = 5050
    // ═══════════════════════════════════════════════════════════

    [Benchmark]
    public int Baseline_Nested100() {
        int s = 0;
        for (int i = 1; i <= 100; i++) s += i;
        return s;
    }

    [Benchmark]
    public object? Vm_Nested100() {
        using var state = new VmState { Program = _progNested };
        return Vm.Execute(state).Value;
    }

    // ═══════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════

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

    private static Bytecode Lower(Node node) {
        var analysis = Analyze(node);
        return Lowering.Lower(node, analysis);
    }

    private static Func<object?> CompileToFunc(Node node, bool preferInterpretation) {
        var analysis = Analyze(node);
        var expr = new LinqExpressionGenerator(analysis).Compile(node).Expression;
        var boxed = expr.Type.IsValueType
            ? Expression.Convert(expr, typeof(object))
            : expr;
        return Expression.Lambda<Func<object?>>(boxed).Compile(preferInterpretation);
    }

    private static Node NestedAdd(int count) {
        if (count <= 0) return new Constant(0);
        Node n = new Constant(count);
        for (int i = count - 1; i >= 1; i--)
            n = new Add(new Constant(i), n);
        return n;
    }
}