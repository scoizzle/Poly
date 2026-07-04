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

    // ── Statement context depth ─────────────────────────────────

    /// <summary>
    /// Tracks how many statement-context ancestors this node is nested within.
    /// 0 means expression context — the node's result is consumed.
    /// &gt;0 means statement context — the node's result will be discarded.
    /// </summary>
    public int StatementDepth { get; private set; }

    /// <summary>True when the current node is inside statement context
    /// (its result will be discarded by a parent).</summary>
    public bool IsInStatementContext => StatementDepth > 0;

    /// <summary>True when the current node is inside expression context
    /// (its result will be consumed by a parent).</summary>
    public bool IsInExpressionContext => StatementDepth == 0;

    /// <summary>
    /// Enter a statement context scope.  Child primitives expanded while
    /// the returned guard is alive see <see cref="IsInStatementContext"/> = true
    /// and may elide result-capture primitives.
    /// Call with <c>using var _ = env.EnterStatementContext();</c>.
    /// </summary>
    public StatementGuard EnterStatementContext() {
        StatementDepth++;
        return new StatementGuard(this);
    }

    /// <summary>RAII guard that restores <see cref="StatementDepth"/> on dispose.
    /// Returned by <see cref="EnterStatementContext"/>.</summary>
    public readonly struct StatementGuard : IDisposable {
        private readonly ExpansionEnvironment _env;
        internal StatementGuard(ExpansionEnvironment env) { _env = env; }
        public void Dispose() => _env.StatementDepth--;
    }

    // ── Loop boundary stack ─────────────────────────────────────

    public bool IsInLoop => _loops.Count > 0;

    public LoopBoundary CurrentLoop => _loops.Peek();

    public void PushLoop(LoopBoundary boundary) => _loops.Push(boundary);
}

/// <summary>Labels for a loop's exit and latch targets.</summary>
internal sealed record LoopBoundary(Label Exit, Label Latch);