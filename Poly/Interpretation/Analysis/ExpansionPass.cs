using Poly.Interpretation.Analysis.ControlFlow;
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
/// Per-traversal depth tracker for <see cref="ExpansionPass"/>.
/// </summary>
internal sealed class ExpansionPassState : IAnalysisMetadata {
    public int Depth { get; set; }
}

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
    public const string Id = "Expansion";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id, SideEffectAnalyzer.Id, JumpTargetAnalyzer.Id, ControlFlowAnalysisPass.Id, ValueRepresentationAnalyzer.Id, CallSiteCatalogAnalyzer.Id, ExceptionRegionAnalyzer.Id];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<ExpansionPass>(node))
            return;

        var actual = context.GetNodeReplacement(node) ?? node;

        if (context.GetMetadata<ElisionMetadata>(actual)?.CanElide == true)
            return;

        var state = context.GetMetadata<ExpansionPassState>(null);
        if (state is null) {
            state = new ExpansionPassState();
            context.SetMetadata<ExpansionPassState>(null, state);
        }

        bool isRootEntry = state.Depth == 0;
        state.Depth++;

        try {
            ExpansionContext pCtx;
            if (isRootEntry) {
                pCtx = new ExpansionContext(context);
                context.SetMetadata<ExpansionContext>(null, pCtx);
            }
            else {
                pCtx = context.GetMetadata<ExpansionContext>(null)
                    ?? throw new InvalidOperationException("ExpansionContext missing during traversal.");
            }

            if (context.GetMetadata<PrimitiveExpansionMetadata>(actual) is null) {
                var primitives = actual.ToPrimitives(pCtx).ToArray();
                context.SetMetadata(actual, new PrimitiveExpansionMetadata(primitives));
            }

            this.AnalyzeChildren(context, node);
        }
        finally {
            state.Depth--;
        }
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