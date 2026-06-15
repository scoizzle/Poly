using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Options controlling dead code / elision analysis and diagnostics.
/// </summary>
public sealed record SideEffectAnalysisOptions {
    public static SideEffectAnalysisOptions Default { get; } = new();
    public bool EmitElisionDiagnostics { get; init; }
}

internal sealed class SideEffectAnalyzer : INodeAnalyzer {
    private static readonly SideEffectMetadata PureMeta = new(SideEffectKind.Pure);
    private static readonly ElisionMetadata Elidable = new(true);
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<SideEffectAnalyzer>(node)) {
            return;
        }

        if (node is Block block) {
            // Most optimum path for Blocks (the high-fanout case for DCE):
            // - Direct indexed loops over concrete collections (no IEnumerable.Children overhead).
            // - Call this.Analyze on direct children exactly once (proper recursion + subtree processing).
            // - Accumulate block-level hasSideEffects and perform elision decisions in the *same* pass.
            // - No generic AnalyzeChildren, no second walk, no ComputeHasSideEffects.

            // Process declared variables (they may contain expressions that need analysis).
            foreach (var v in block.Variables) {
                if (v != null && context.ShouldAnalyze(v)) {
                    this.Analyze(context, v);
                }
            }

            SideEffectKind blockKind = SideEffectKind.Pure;
            var opts = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags = opts.EmitElisionDiagnostics;

            var nodes = block.Nodes;
            int n = nodes.Count;

            for (int i = 0; i < n; i++) {
                var child = nodes[i];
                if (child is null || !context.ShouldAnalyze(child)) continue;

                this.Analyze(context, child);
                var childMeta = context.GetMetadata<SideEffectMetadata>(child);
                var childKind = childMeta?.Kind ?? SideEffectKind.External;
                blockKind = ClassifyWorst(blockKind, childKind);

                // Track whether an Assignment's value is used by the enclosing block.
                // Non-last Assignments in a Block never have their value consumed.
                if (child is Assignment) {
                    context.SetMetadata(child, new AssignmentValueUsedMetadata(i == n - 1));
                }

                if (i < n - 1 && (childKind == SideEffectKind.Pure || childKind == SideEffectKind.Read)) {
                    context.SetMetadata(child, Elidable);
                    if (emitDiags) {
                        context.ReportInformation(
                            child,
                            "Pure expression with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
            }

            context.SetMetadata(node, new SideEffectMetadata(blockKind));
        }
        else {
            // For all other nodes: aggregate children's side effect kinds.
            SideEffectKind kind = ClassifyIntrinsic(node);
            this.AnalyzeChildren(context, node);
            foreach (var child in node.Children) {
                if (child is null) continue;
                var childMeta = context.GetMetadata<SideEffectMetadata>(child);
                if (childMeta is not null)
                    kind = ClassifyWorst(kind, childMeta.Kind);
            }

            if (kind == SideEffectKind.Pure && node is Member memberAccess) {
                var resolved = context.GetResolvedMember(memberAccess);
                if (resolved?.Mutability.HasFlag(Mutability.VolatileAccess) == true) {
                    kind = SideEffectKind.Read;
                }
            }
            context.SetMetadata(node, new SideEffectMetadata(kind));

            if (!context.ShouldAnalyze(node)) return;

            var opts2 = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags2 = opts2.EmitElisionDiagnostics;

            // Mark loop control subexpressions (initializer, increment for ForLoop) as elidable
            // if they are pure and their value is unused in the loop context.
            // This enables more aggressive intra-loop DCE even when the whole loop node is kept
            // (e.g. because it is the last expression in its enclosing Block, or the loop
            // "produces" a value from its body).
            var opts = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags = opts.EmitElisionDiagnostics;

            if (node is ForLoop forLoop) {
                var initMeta = forLoop.Initializer is not null ? context.GetMetadata<SideEffectMetadata>(forLoop.Initializer) : null;
                if (initMeta is not null && (initMeta.Kind == SideEffectKind.Pure || initMeta.Kind == SideEffectKind.Read)) {
                    context.SetMetadata(forLoop.Initializer, Elidable);
                    if (emitDiags2 && forLoop.Initializer is not null) {
                        context.ReportInformation(
                            forLoop.Initializer,
                            "Pure initializer with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
                var incMeta = forLoop.Increment is not null ? context.GetMetadata<SideEffectMetadata>(forLoop.Increment) : null;
                if (incMeta is not null && (incMeta.Kind == SideEffectKind.Pure || incMeta.Kind == SideEffectKind.Read)) {
                    context.SetMetadata(forLoop.Increment, Elidable);
                    if (emitDiags2 && forLoop.Increment is not null) {
                        context.ReportInformation(
                            forLoop.Increment,
                            "Pure increment with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
                // Condition's value is used for control flow; do not mark the condition
                // expression itself as elidable (though its internal pure unused subparts
                // can still be elided during its own compilation).
            }
            else if (node is ForEachLoop forEach) {
                // Collection's value is used to drive the iteration; do not mark as elidable.
            }
            // WhileLoop / DoWhileLoop have no initializer/increment parts whose values are
            // discarded independently of control.
        }
    }

    private static bool GetHasSideEffects(AnalysisContext context, Node? node) {
        if (node is null) return false;
        var meta = context.GetMetadata<SideEffectMetadata>(node);
        return meta is null || meta.Kind != SideEffectKind.Pure;
    }

    private static SideEffectKind ClassifyWorst(SideEffectKind a, SideEffectKind b) =>
        (SideEffectKind)int.Max((int)a, (int)b);

    private static SideEffectKind ClassifyIntrinsic(Node node) => node switch {
        Assignment => SideEffectKind.Write,
        SuspendNode => SideEffectKind.External,
        Return => SideEffectKind.Write,
        IndexAccess => SideEffectKind.Read,
        New => SideEffectKind.Allocate,
        WhileLoop or DoWhileLoop or ForLoop or ForEachLoop => SideEffectKind.Write,
        IfStatement or SwitchStatement or ThrowStatement => SideEffectKind.External,
        Invoke => SideEffectKind.External,
        Await => SideEffectKind.External,
        TryCatchFinally or UsingStatement => SideEffectKind.External,
        BreakStatement or ContinueStatement or GotoStatement or LabelDeclaration => SideEffectKind.Write,
        _ => SideEffectKind.Pure
    };
}

public enum SideEffectKind {
    Pure,
    Read,
    Write,
    Allocate,
    External,
}

public sealed record SideEffectMetadata(SideEffectKind Kind) : IAnalysisMetadata;

/// <summary>
/// Metadata indicating that a node's execution (and subtree) can be safely elided
/// during interpretation or lowering without changing observable behavior or results.
/// </summary>
public sealed record ElisionMetadata(bool CanElide) : IAnalysisMetadata;

/// <summary>
/// Metadata indicating whether an <see cref="Assignment"/> node's value is consumed
/// by a parent expression. When <c>false</c>, the lowering can skip the <c>DupOp</c>
/// that preserves the assignment's value, eliminating the redundant <c>dup; store; pop</c>
/// pattern.
/// </summary>
public sealed record AssignmentValueUsedMetadata(bool IsValueUsed) : IAnalysisMetadata;

public static class SideEffectAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseSideEffectAnalysis() {
            builder.AddAnalyzer(new SideEffectAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        public bool HasSideEffects(Node? node) {
            if (node is null) return false;
            var meta = provider.GetMetadata<SideEffectMetadata>(node);
            return meta is null || meta.Kind != SideEffectKind.Pure;
        }

        /// <summary>
        /// Returns true if the node/subtree can be elided (pure with unused result, e.g. non-final in Block).
        /// </summary>
        public bool CanElide(Node? node) {
            if (node is null) {
                return false;
            }
            return provider.GetMetadata<ElisionMetadata>(node)?.CanElide ?? false;
        }
    }
}