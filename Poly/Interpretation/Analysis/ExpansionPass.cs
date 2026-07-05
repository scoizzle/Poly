using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Primitives;

namespace Poly.Interpretation.Analysis;

/// <summary>
/// Metadata holding the expanded primitive sequence for a node.
/// Produced by <see cref="ExpansionPass"/> and consumed by
/// <see cref="Vm.ProgramCompiler.CompilePrimitives"/>.
/// </summary>
/// <param name="Primitives">The expanded primitive sequence (labels unresolved).</param>
public sealed record PrimitiveExpansionMetadata(
    IReadOnlyList<PrimitiveNode> Primitives
) : IAnalysisMetadata;

/// <summary>
/// Analysis pass that drives <see cref="Node.ToPrimitives"/> for every node
/// in the AST, storing the resulting <see cref="PrimitiveExpansionMetadata"/>.
///
/// This pass integrates with the incremental analysis framework — only dirty
/// subtrees are re-expanded on subsequent runs.
///
/// Register via <c>builder.AddAnalyzer(new ExpansionPass())</c>.
/// </summary>
public sealed class ExpansionPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ExpansionPass>(node))
            return;

        // Expand this node first (pre-order) — parent nodes like Block set up
        // expansion environments (slot assignment, loop boundary registration)
        // that children depend on during their own expansion.
        var actual = context.GetNodeReplacement(node) ?? node;

        // Skip expansion of dead/unreachable code — CFG analysis stamps
        // ElisionMetadata on unreachable subtrees.  No µops needed.
        if (context.GetMetadata<ElisionMetadata>(actual)?.CanElide == true)
            return;

        if (context.GetMetadata<PrimitiveExpansionMetadata>(actual) is null) {
            var pCtx = new ExpansionContext(context);
            // Store the context so Interpreter.CompileCore can extract pending
            // function bodies compiled from child lambdas.
            context.SetMetadata<ExpansionContext>(null, pCtx);
            var primitives = actual.ToPrimitives(pCtx).ToArray();
            context.SetMetadata(actual, new PrimitiveExpansionMetadata(primitives));
        }

        // Recurse into children
        this.AnalyzeChildren(context, node);
    }
}

public static class ExpansionPassExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds the <see cref="ExpansionPass"/> to the analysis pipeline,
        /// enabling primitive expansion (AST → <see cref="PrimitiveNode"/> sequence)
        /// during analysis.  Expanded primitives are available via
        /// <c>analysis.GetMetadata&lt;PrimitiveExpansionMetadata&gt;(node)</c>.
        /// </summary>
        public AnalyzerBuilder UsePrimitiveExpansion() => builder
            .AddAnalyzer(new ExpansionPass());
    }
}