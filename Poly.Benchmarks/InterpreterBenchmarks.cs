using System.Linq.Expressions;

using BenchmarkDotNet.Attributes;

using Poly.Interpretation;
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
    private Node? _poly, _polyParam, _clrChain, _clrSimple, _nested100;
    private Parameter? _polyX;
    private Bytecode? _progPoly, _progClrChain, _progClrSimple, _progNested;
    private Func<object?>? _linqPoly, _linqInterpPoly;
    private Func<int, int>? _linqPolyJit, _linqPolyInterp;

    [GlobalSetup]
    public void Setup() {
        // Constant polynomial for RISC VM: 3*5*5*5 + 2*5*5 + 5 + 5 = 435
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

        // Parameterized polynomial: 3*x*x*x + 2*x*x + x + 5, x=5 => 435
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

        // CLR chain: Math.Max(Math.Min(100, 200), 50)  (nested CLR calls)
        _clrSimple = new Invoke(
            new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Max)),
            new Invoke(
                new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Min)),
                new Constant(100), new Constant(200)
            ),
            new Constant(50)
        );

        // CLR chain: Math.Pow(Math.Max(Math.Min(100,200), 50), 2) = 10000
        _clrChain = new Invoke(
            new Member(new TypeReference(typeof(Math).FullName!), nameof(Math.Pow)),
            _clrSimple, new Constant(2)
        );

        // 1+2+...+100 = 5050  (deep arithmetic)
        _nested100 = NestedAdd(100);

        // Pre-lower RISC programs
        _progPoly = Lower(_poly);
        _progClrChain = Lower(_clrChain);
        _progClrSimple = Lower(_clrSimple);
        _progNested = Lower(_nested100);

        // Pre-compile LINQ delegates (pure arithmetic only)
        _linqPoly = CompileToFunc(_poly, false);
        _linqInterpPoly = CompileToFunc(_poly, true);

        // Parameterized LINQ delegates
        var paramAnalysis = Analyze(_polyParam!);
        var gen = new LinqExpressionGenerator(paramAnalysis);
        _linqPolyJit = gen.CompileAsDelegate<Func<int, int>>(_polyParam!, _polyX!);
        _linqPolyInterp = (Func<int, int>)gen
            .CompileAsLambda(_polyParam!, _polyX!)
            .Compile(preferInterpretation: true);
    }

    // ───────── Polynomial (constant): 3*5*5*5 + 2*5*5 + 5 + 5 = 435 ─────────

    [Benchmark]
    public object? Vm_Poly() {
        using var state = new VmState();
        state.Program = _progPoly;
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? TreeWalk_PolyParam() {
        using var state = new VmState();
        state.Program = _progPoly;
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? LinqJit_PolyParam() => _linqPolyJit!(5);

    [Benchmark]
    public object? LinqInterp_PolyParam() => _linqPolyInterp!(5);

    // ───────── CLR: Math.Max(Math.Min(100, 200), 50) = 100 ─────────

    [Benchmark]
    public object? TreeWalk_ClrSimple() {
        using var state = new VmState();
        state.Program = _progClrSimple;
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? Vm_ClrSimple() {
        using var state = new VmState();
        state.Program = _progClrSimple;
        return Vm.Execute(state).Value;
    }

    // ───────── CLR chain: Math.Pow(Max(Min(100,200),50), 2) = 10000 ─────────

    [Benchmark]
    public object? TreeWalk_ClrChain() {
        using var state = new VmState();
        state.Program = _progClrChain;
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? Vm_ClrChain() {
        using var state = new VmState();
        state.Program = _progClrChain;
        return Vm.Execute(state).Value;
    }

    // ───────── Nested add: 1+...+100 = 5050 ─────────

    [Benchmark]
    public object? TreeWalk_Nested100() {
        using var state = new VmState();
        state.Program = _progNested;
        return Vm.Execute(state).Value;
    }

    [Benchmark]
    public object? Vm_Nested100() {
        using var state = new VmState();
        state.Program = _progNested;
        return Vm.Execute(state).Value;
    }

    // ───────── Helpers ─────────

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