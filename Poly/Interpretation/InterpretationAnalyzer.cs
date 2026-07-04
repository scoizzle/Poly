using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Primitives;

using PrimReturn = Poly.Syntax.Primitives.Return;

namespace Poly.Interpretation;

/// <summary>
/// Standard analysis pipeline for VM execution.
///
/// Bundles all interpretation-level analysis passes required for the
/// AST → primitives → compiled delegate path:
///   <list type="bullet">
///     <item><see cref="Semantics.UseTypeAndMemberResolver"/></item>
///     <item><see cref="Semantics.UseVariableScopeValidator"/></item>
///     <item><see cref="Semantics.UseSideEffectAnalysis"/></item>
///     <item><see cref="Semantics.UseThisReferenceContext"/></item>
///     <item><see cref="Semantics.UseJumpTargetResolution"/></item>
///     <item><see cref="ControlFlow.UseControlFlowAnalysis"/></item>
///     <item><see cref="ConstantFolding.UseConstantFolding"/></item>
///     <item><see cref="Semantics.UseDefiniteAssignmentAnalysis"/></item>
///     <item><see cref="Semantics.UseLambdaReturnTypeResolution"/></item>
///     <item><see cref="Analysis.UsePrimitiveExpansion"/></item>
///   </list>
///
/// The <see cref="Analyzer"/> is built once and cached; subsequent calls reuse it.
/// </summary>
public static class InterpretationAnalyzer {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseThisReferenceContext()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UsePrimitiveExpansion()
        .Build();

    /// <summary>
    /// Gets the cached <see cref="Analyzer"/> instance for the standard VM pipeline.
    /// </summary>
    public static Analyzer Analyzer => _analyzer;

    /// <summary>
    /// Analyze <paramref name="node"/> through the standard VM pipeline and return
    /// the <see cref="AnalysisResult"/>.  Primitive expansion metadata is guaranteed
    /// to be present for the root node on the returned result.
    /// </summary>
    public static AnalysisResult Analyze(Node node) =>
        _analyzer.Analyze(node);

    /// <summary>
    /// Analyze <paramref name="node"/> through the standard VM pipeline and compile
    /// the expanded primitives into a <see cref="VmProgram"/>.
    /// </summary>
    public static VmProgram Compile(Node node, CompilationMode mode = CompilationMode.Normal) {
        var analysis = _analyzer.Analyze(node);
        return CompileCore(node, analysis, mode);
    }

    /// <summary>
    /// Compile a previously-analyzed <paramref name="node"/> (that was analyzed with
    /// the standard pipeline) into a <see cref="VmProgram"/>.  Unlike <see cref="Compile(Node, CompilationMode)"/>,
    /// this does not re-run the analysis passes.
    /// </summary>
    public static VmProgram Compile(Node node, AnalysisResult analysis, CompilationMode mode = CompilationMode.Normal) =>
        CompileCore(node, analysis, mode);

    /// <summary>
    /// Analyze, compile, and execute <paramref name="node"/>, returning the
    /// top-of-stack value.
    /// </summary>
    public static long Execute(Node node) {
        using var result = Vm.Vm.Execute(Compile(node, CompilationMode.Normal));
        return result.RawValue;
    }

    // ── Shared implementation ─────────────────────────────────────

    private static VmProgram CompileCore(Node node, AnalysisResult analysis, CompilationMode mode) {
        var meta = analysis.GetMetadata<PrimitiveExpansionMetadata>(node);
        IReadOnlyList<PrimitiveNode> primitives;
        if (meta is not null) {
            primitives = meta.Primitives;
        }
        else {
            // Fallback: expand directly if the pass didn't capture metadata
            // (e.g. when the root node was replaced during analysis).
            // This should not happen in normal usage — ExpansionPass should
            // have stamped metadata during analysis. We fall back so the
            // system doesn't crash, but the oversight is surfaced.
            System.Diagnostics.Debug.WriteLine(
                "[InterpretationAnalyzer] Warning: PrimitiveExpansionMetadata not found; " +
                "ExpansionPass may not have run. Falling back to direct expansion.");
            var ctx = new AnalysisContext(Introspection.CommonLanguageRuntime.ClrTypeDefinitionRegistry.Shared);
            primitives = node.ToPrimitives(ctx).ToArray();
        }

        var primsList = primitives.ToList();
        primsList.Add(new PrimReturn());
        return ProgramCompiler.CompilePrimitives(primsList, mode: mode);
    }
}