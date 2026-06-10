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


        var paramAnalysis = Analyze(_polyParam!);
        var gen = new LinqExpressionGenerator(paramAnalysis);
        _linqPolyJitParam = gen.CompileAsDelegate<Func<int, int>>(_polyParam!, _polyX!);
        _linqPolyInterpParam = (Func<int, int>)gen
            .CompileAsLambda(_polyParam!, _polyX!)
            .Compile(preferInterpretation: true);
    }

    [Benchmark(Baseline = true)]
    public int Baseline_Poly() => 3 * 5 * 5 * 5 + 2 * 5 * 5 + 5 + 5;

    [Benchmark]
    public object? LinqJit_Poly() => _linqPolyJit!();

    [Benchmark]
    public object? LinqInterp_Poly() => _linqPolyInterp!();

    [Benchmark]
    public object? Vm_Poly() {
        using var state = new VmState { Program = _vmPolyProgram };
        return Vm.Execute(state).Value;
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