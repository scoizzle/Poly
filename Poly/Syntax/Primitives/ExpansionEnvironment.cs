using Poly.Syntax.Analysis;

namespace Poly.Syntax.Primitives;

/// <summary>
/// Mutable expansion environment stored as pass-level metadata
/// (NodeId.Empty) during ToPrimitives().  Coordinates variable slot
/// assignment and loop boundary tracking across parent/child AST nodes.
///
/// Slots are keyed by <see cref="NodeId"/> so that expansion is robust
/// across node identity transformations.
/// </summary>
internal sealed class ExpansionEnvironment : IAnalysisMetadata {
    private readonly Dictionary<NodeId, int> _slots = new();
    private int _nextSlot;
    private readonly Stack<LoopBoundary> _loops = new();

    // ── Slot management ─────────────────────────────────────────

    public bool HasSlot(Node node) => _slots.ContainsKey(node.Id);

    public bool TryGetSlot(Node node, out int slot) =>
        _slots.TryGetValue(node.Id, out slot);

    /// <summary>Returns the existing slot for <paramref name="node"/>,
    /// or assigns and returns a new one.</summary>
    public int GetOrAssignSlot(Node node) {
        if (_slots.TryGetValue(node.Id, out var slot))
            return slot;
        slot = _nextSlot++;
        _slots[node.Id] = slot;
        return slot;
    }

    /// <summary>Allocate a temp slot (e.g. for φ merge) without
    /// associating it with a particular AST node.</summary>
    public int AllocateTempSlot() => _nextSlot++;

    // ── Loop boundary stack ─────────────────────────────────────

    public bool IsInLoop => _loops.Count > 0;

    public LoopBoundary CurrentLoop => _loops.Peek();

    public void PushLoop(LoopBoundary boundary) => _loops.Push(boundary);
}

/// <summary>Labels for a loop's exit and latch targets.</summary>
internal sealed record LoopBoundary(Label Exit, Label Latch);