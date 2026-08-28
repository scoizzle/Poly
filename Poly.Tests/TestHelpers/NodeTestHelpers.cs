using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Interpretation.Vm;

using Expr = System.Linq.Expressions.Expression;
using Exprs = System.Linq.Expressions;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Helper methods for testing Node-based expressions using the analyzer and code generation pattern.
/// </summary>
public static class AnalyzerBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseAllAnalyzers() => builder
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .UseDefiniteAssignmentAnalysis()
            .UseLambdaReturnTypeResolution()
            .UseValueRepresentationAnalysis()
            .UseCallSiteCatalog()
            ;

    }
}

public static class NodeTestHelpers {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder().UseAllAnalyzers().Build();

    /// <summary>
    /// Analyzes a node using the standard test analyzer pipeline.
    /// </summary>
    public static AnalysisResult AnalyzeNode(this Node node) {
        return _analyzer.Analyze(node);
    }

    /// <summary>
    /// Builds a LINQ Expression Tree from a node using the standard analyzer and generator pipeline.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <returns>A LINQ Expression representation.</returns>
    public static Expr BuildExpression(this Node node) {
        var analysisResult = node.AnalyzeNode();
        var generator = new LinqExpressionGenerator(analysisResult);
        return generator.Compile(node).Expression;
    }

    /// <summary>
    /// Builds a LINQ Expression and collects generated parameter expressions based on declared parameters.
    /// </summary>
    /// <param name="node">The node to transform.</param>
    /// <param name="parameters">Parameter declarations (node, CLR type) to register before analysis.</param>
    /// <returns>Tuple of expression and generated parameter expressions.</returns>
    public static (Expr Expression, Exprs.ParameterExpression[] Parameters) BuildExpressionWithParameters(
        this Node node,
        params (Parameter param, Type clrType)[] parameters) {

        // Pre-register parameter types with a setup action before analysis
        var analysisResult = _analyzer.Analyze(node, setup: ctx => {
            foreach (var (param, clrType) in parameters) {
                var typeDef = ctx.TypeDefinitions.GetTypeDefinition(clrType);
                if (typeDef != null) {
                    ctx.SetResolvedType(param, typeDef);
                }
            }
        });

        var generator = new LinqExpressionGenerator(analysisResult);
        var compilation = generator.Compile(node);
        var expression = compilation.Expression;
        var generatedParams = compilation.Parameters.ToArray();

        // Build a mapping of parameter names to generated expressions
        var paramMap = new Dictionary<string, Exprs.ParameterExpression>();
        foreach (var p in generatedParams) {
            paramMap[p.Name!] = p;
        }

        // Ensure all requested parameters are present
        var result = new List<Exprs.ParameterExpression>();
        foreach (var (param, clrType) in parameters) {
            var paramName = param.Name ?? throw new ArgumentNullException(nameof(param));
            if (paramMap.TryGetValue(paramName, out var generated)) {
                result.Add(generated);
            }
            else {
                // Parameter wasn't used in the expression, create it manually
                result.Add(Exprs.Expression.Parameter(clrType, paramName));
            }
        }

        return (expression, result.ToArray());
    }

    /// <summary>
    /// Compiles a node into a delegate, registering provided parameters and using emitted parameter expressions.
    /// </summary>
    public static TDelegate CompileLambda<TDelegate>(this Node node, params (Parameter param, Type clrType)[] parameters)
        where TDelegate : Delegate {
        var (expression, parameterExpressions) = node.BuildExpressionWithParameters(parameters);
        return (TDelegate)System.Linq.Expressions.Expression.Lambda(expression, parameterExpressions).Compile();
    }

    // ── New primitive pipeline helpers ──────────────────────────────

    /// <summary>
    /// Compile a node using the standard VM interpretation pipeline.
    /// </summary>
    public static VmProgram CompileWithPrimitives(this Node node, CompilationMode mode = CompilationMode.Normal) =>
        Interpreter.Compile(node, mode);

    /// <summary>
    /// Execute a node via the standard VM interpretation pipeline end-to-end,
    /// returning the result.
    /// </summary>
    public static InterpreterResult ExecWithPrimitives(this Node node) {
        using var exec = Interpreter.Execute(node.CompileWithPrimitives());
        return exec.Result;
    }

    /// <summary>Same-tree LINQ + VM oracle for integer results (VM ABI as long).</summary>
    public static async Task AssertDualOracleInt(this Node node, int expected) {
        var linq = Expr.Lambda<Func<int>>(node.BuildExpression()).Compile()();
        await Assert.That(linq).IsEqualTo(expected);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo((long)expected);
    }

    public static async Task AssertDualOracleDouble(this Node node, double expected) {
        var linq = Expr.Lambda<Func<double>>(node.BuildExpression()).Compile()();
        await Assert.That(linq).IsEqualTo(expected);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<double>()).IsEqualTo(expected);
    }
}