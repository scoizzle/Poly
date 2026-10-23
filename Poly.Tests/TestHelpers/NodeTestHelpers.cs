using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Helper methods for testing Node-based expressions using the analyzer and code generation pattern.
/// </summary>
public static class AnalyzerBuilderExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseAllAnalyzers() => builder
            .UseThisReferenceContext()
            .UseVariableScopeValidator()
            .UseTypeAndMemberResolver()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .UseDefiniteAssignmentAnalysis()
            .UseLambdaReturnTypeResolution()
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
}