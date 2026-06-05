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
    // Flyweight singletons for the only metadata values we ever emit.
    // Pure nodes (no side effects) and elidable nodes share these instances.
    // This eliminates per-node allocations for this analyzer's metadata.
    // Other analyzers can adopt the same pattern for small immutable metadata.
    private static readonly SideEffectMetadata NoSideEffects = new(false);
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

            bool blockHasSideEffects = false;
            var opts = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags = opts.EmitElisionDiagnostics;

            var nodes = block.Nodes;
            int n = nodes.Count;

            for (int i = 0; i < n; i++) {
                var child = nodes[i];
                if (child is null || !context.ShouldAnalyze(child)) continue;

                this.Analyze(context, child);           // recurse exactly once for this child subtree
                bool childHas = GetHasSideEffects(context, child);
                blockHasSideEffects |= childHas;

                if (i < n - 1 && !childHas) {
                    context.SetMetadata(child, Elidable);
                    if (emitDiags) {
                        context.ReportInformation(
                            child,
                            "Pure expression with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
            }

            if (!blockHasSideEffects) {
                context.SetMetadata(node, NoSideEffects);
            }
        }
        else {
            // For all other nodes: use AggregateChildren for a single fused pass that
            // dispatches Analyze to children *and* aggregates the "has side effects" value
            // on the way. No separate AnalyzeChildren + re-walk.
            bool childrenHave = this.AggregateChildren(
                context,
                node,
                (ctx, ch) => {
                    this.Analyze(ctx, ch); // ensure full processing + metadata on the child
                    return GetHasSideEffects(ctx, ch);
                },
                (a, b) => a || b,
                false);

            bool hasSideEffects = childrenHave || IsIntrinsicallySideEffecting(node);
            if (!hasSideEffects && node is Member memberAccess) {
                var resolved = context.GetResolvedMember(memberAccess);
                if (resolved?.Mutability.HasFlag(Mutability.VolatileAccess) == true) {
                    hasSideEffects = true;
                }
            }
            if (!hasSideEffects) {
                context.SetMetadata(node, NoSideEffects);
            }

            // Mark loop control subexpressions (initializer, increment for ForLoop) as elidable
            // if they are pure and their value is unused in the loop context.
            // This enables more aggressive intra-loop DCE even when the whole loop node is kept
            // (e.g. because it is the last expression in its enclosing Block, or the loop
            // "produces" a value from its body).
            var opts = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags = opts.EmitElisionDiagnostics;

            if (node is ForLoop forLoop) {
                if (forLoop.Initializer != null && !GetHasSideEffects(context, forLoop.Initializer)) {
                    context.SetMetadata(forLoop.Initializer, Elidable);
                    if (emitDiags) {
                        context.ReportInformation(
                            forLoop.Initializer,
                            "Pure initializer with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
                if (forLoop.Increment != null && !GetHasSideEffects(context, forLoop.Increment)) {
                    context.SetMetadata(forLoop.Increment, Elidable);
                    if (emitDiags) {
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
        return context.GetMetadata<SideEffectMetadata>(node)?.HasSideEffects ?? true;
    }

    private static bool IsIntrinsicallySideEffecting(Node node) => node switch {
        Assignment => true,
        SuspendNode => true,
        Return => true,
        IndexAccess => true,
        New => true,
        _ => false
    };
}

public sealed record SideEffectMetadata(bool HasSideEffects) : IAnalysisMetadata;

/// <summary>
/// Metadata indicating that a node's execution (and subtree) can be safely elided
/// during interpretation or lowering without changing observable behavior or results.
/// </summary>
public sealed record ElisionMetadata(bool CanElide) : IAnalysisMetadata;

public static class SideEffectAnalysisExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseSideEffectAnalysis() {
            builder.AddAnalyzer(new SideEffectAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        public bool HasSideEffects(Node? node) {
            if (node is null) {
                return false;
            }
            // Only pure nodes get explicit SideEffectMetadata(false).
            // Default true = has side effects.
            return provider.GetMetadata<SideEffectMetadata>(node)?.HasSideEffects ?? true;
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