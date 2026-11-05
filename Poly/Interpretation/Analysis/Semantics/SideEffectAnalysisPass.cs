namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>Options controlling dead code / elision analysis and diagnostics.</summary>
public sealed record SideEffectAnalysisOptions {
    /// <summary>Default options with elision diagnostics disabled.</summary>
    public static SideEffectAnalysisOptions Default { get; } = new();

    /// <summary>When true, emits <c>DEAD_CODE_ELIDABLE</c> diagnostics for
    /// pure expressions whose results are unused and could be elided.</summary>
    public bool EmitElisionDiagnostics { get; init; }
}

/// <summary>
/// Classifies what each node's execution can do and composes the answer post-order, so a consumer
/// asks one bag instead of re-walking a tree. The per-kind half is <see cref="NodeFacts.Effects"/>;
/// this pass adds the children, and the one place composition is not a plain union is
/// <see cref="TryCatchFinally"/>, where a catch-all clause absorbs the try block's throw.
/// </summary>
internal sealed class SideEffectAnalyzer : INodeAnalyzer {
    public const string Id = "SideEffect";
    public string PassName => Id;

    /// <summary>What a node with no metadata answers: fail closed on both axes.</summary>
    private const NodeEffect Unanalyzed = NodeEffect.External | NodeEffect.Throws;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Block block) {
            var blockEffects = NodeEffect.None;
            foreach (var v in block.Variables) {
                if (v is null) continue;
                Analyze(context, v);
                blockEffects |= EffectsOf(context, v);
            }

            var opts = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags = opts.EmitElisionDiagnostics;

            var nodes = block.Nodes;
            int n = nodes.Count;

            for (int i = 0; i < n; i++) {
                var child = nodes[i];
                if (child is null) continue;

                Analyze(context, child);
                var childEffects = EffectsOf(context, child);
                blockEffects |= childEffects;

                if (child is Assignment) {
                    context.SetMetadata(child, new AssignmentValueUsedMetadata(i == n - 1));
                }

                // A statement whose result is unused can go only when executing it has no effect at
                // all. A decimal addition looks inert and throws; a read observes state on its own.
                if (i < n - 1 && childEffects == NodeEffect.None) {
                    ElisionFacts.Mark(context, child, ElisionReason.NoObservableEffect);
                    if (emitDiags) {
                        context.ReportInformation(
                            child,
                            "Pure expression with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
            }

            context.SetMetadata(node, new SideEffectMetadata(blockEffects));
        }
        else {
            this.AnalyzeChildren(context, node);
            context.SetMetadata(node, new SideEffectMetadata(Compose(context, node)));

            var opts2 = context.Settings.Get<SideEffectAnalysisOptions>() ?? SideEffectAnalysisOptions.Default;
            bool emitDiags2 = opts2.EmitElisionDiagnostics;

            if (node is ForLoop forLoop) {
                var initEffects = EffectsOf(context, forLoop.Initializer);
                if (forLoop.Initializer is not null && initEffects == NodeEffect.None) {
                    ElisionFacts.Mark(context, forLoop.Initializer, ElisionReason.NoObservableEffect);
                    if (emitDiags2) {
                        context.ReportInformation(
                            forLoop.Initializer,
                            "Pure initializer with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
                var incEffects = EffectsOf(context, forLoop.Increment);
                if (forLoop.Increment is not null && incEffects == NodeEffect.None) {
                    ElisionFacts.Mark(context, forLoop.Increment, ElisionReason.NoObservableEffect);
                    if (emitDiags2) {
                        context.ReportInformation(
                            forLoop.Increment,
                            "Pure increment with unused result can be elided (dead code).",
                            "DEAD_CODE_ELIDABLE");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The node's intrinsic effects plus its children's, with two exceptions. A catch-all clause
    /// absorbs the try block's throw, because the block's exception cannot leave the statement. A
    /// lambda or a type declaration keeps its body's state effects but not its throw: the body runs
    /// when something calls it.
    /// </summary>
    private static NodeEffect Compose(AnalysisContext context, Node node) {
        var effects = NodeFacts.Effects(node);

        if (node is TryCatchFinally tryCatch) {
            var tryEffects = EffectsOf(context, tryCatch.TryBlock);
            if (tryCatch.CatchClauses?.Any(clause => clause.ExceptionType is null) == true)
                tryEffects &= ~NodeEffect.Throws;
            effects |= tryEffects;

            if (tryCatch.CatchClauses is not null) {
                foreach (var clause in tryCatch.CatchClauses) {
                    effects |= EffectsOf(context, clause.ExceptionType);
                    effects |= EffectsOf(context, clause.Body);
                }
            }
            effects |= EffectsOf(context, tryCatch.FinallyBlock);
        }
        else {
            foreach (var child in node.Children) {
                effects |= EffectsOf(context, child);
            }
        }

        if (node is Lambda or TypeDefinitionNode)
            effects &= ~NodeEffect.Throws;

        return effects;
    }

    private static NodeEffect EffectsOf(AnalysisContext context, Node? node) =>
        node is null
            ? NodeEffect.None
            : context.GetMetadata<SideEffectMetadata>(node)?.Effects ?? Unanalyzed;
}

/// <summary>
/// What a node's execution can do, composed over its subtree. <see cref="HasStateEffect"/> and
/// <see cref="Throws"/> are separate questions because a consumer usually needs only one:
/// control-flow pruning asks whether a condition can change, elision asks whether the whole thing
/// is free. <see cref="IsFree"/> is that second, single answer.
/// </summary>
public sealed record SideEffectMetadata(NodeEffect Effects) : IAnalysisMetadata {
    /// <summary>Reads, writes, allocates or reaches external code. A throw alone is not a state effect.</summary>
    public bool HasStateEffect => (Effects & ~NodeEffect.Throws) != NodeEffect.None;

    /// <summary>Can throw out of its own evaluation.</summary>
    public bool Throws => (Effects & NodeEffect.Throws) != NodeEffect.None;

    /// <summary>Neither changes nor observes state and cannot throw: safe to delete.</summary>
    public bool IsFree => Effects == NodeEffect.None;
}

/// <summary>Why a node may be elided. The reasons accumulate on one
/// <see cref="ElisionMetadata"/> so a reader can see every reason that applies.</summary>
[Flags]
public enum ElisionReason {
    None = 0,
    /// <summary>The node cannot execute: the control flow graph proved it unreachable.</summary>
    Unreachable = 1,
    /// <summary>The node executes, its result is unused, and it is free: it changes nothing, observes
    /// nothing, and cannot throw. Every condition matters, because the writer deletes the statement.</summary>
    NoObservableEffect = 2,
}

/// <summary>
/// Metadata indicating that a node's execution (and subtree) can be safely elided
/// during interpretation or lowering without changing observable behavior or results.
/// </summary>
public sealed record ElisionMetadata(ElisionReason Reasons) : IAnalysisMetadata {
    public bool CanElide => Reasons != ElisionReason.None;
}

/// <summary>
/// The only writer of <see cref="ElisionMetadata"/>: both analysis passes contribute a
/// reason here, so the precedence rule has one home. Precedence: either reason makes a
/// node elidable and every reason that applies is recorded; <see cref="ElisionReason.Unreachable"/>
/// dominates, because a node that never executes is elidable whatever its effects, while
/// <see cref="ElisionReason.NoObservableEffect"/> is only a reason for a node that does execute.
/// </summary>
public static class ElisionFacts {
    public static void Mark(AnalysisContext context, Node node, ElisionReason reason) {
        var reasons = (context.GetMetadata<ElisionMetadata>(node)?.Reasons ?? ElisionReason.None) | reason;
        context.SetMetadata(node, new ElisionMetadata(reasons));
    }
}

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
        /// <summary>True when the node can change or observe state. A throw alone does not count:
        /// control-flow pruning asks whether a condition can change, not whether it can fail.</summary>
        public bool HasSideEffects(Node? node) {
            if (node is null) return false;
            var meta = provider.GetMetadata<SideEffectMetadata>(node);
            return meta is null || meta.HasStateEffect;
        }

        /// <summary>
        /// Returns true if the node/subtree can be elided (free, with an unused result, e.g.
        /// non-final in a Block).
        /// </summary>
        public bool CanElide(Node? node) {
            if (node is null) {
                return false;
            }
            return provider.GetMetadata<ElisionMetadata>(node)?.CanElide ?? false;
        }
    }
}