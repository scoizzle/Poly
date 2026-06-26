using Poly.Syntax.Analysis;

namespace Poly.Syntax.Primitives;

/// <summary>
/// Mutable expansion environment stored as pass-level metadata
/// (NodeId.Empty) during Expand().  Coordinates variable slot assignment
/// and loop boundary tracking across parent/child AST nodes.
/// </summary>
internal sealed class ExpandEnv : IAnalysisMetadata {
    /// <summary>Slot index assigned to each Variable or Parameter, or -1 if not yet assigned.</summary>
    public readonly Dictionary<Node, int> Slots = new();
    /// <summary>Next slot index to assign.</summary>
    public int NextSlot;

    /// <summary>Loop boundary stack for break/continue resolution.</summary>
    public readonly Stack<LoopBoundary> Loops = new();
}

/// <summary>Labels for a loop's exit and latch targets.</summary>
internal sealed record LoopBoundary(Label Exit, Label Latch);