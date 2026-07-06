namespace Poly.Syntax.Primitives;

/// <summary>
/// Mutable expansion environment stored as pass-level metadata
/// (NodeId.Empty) during ToPrimitives().  Coordinates variable slot
/// assignment, loop boundary tracking, and closure capture detection.
///
/// Supports child environments for lambda body expansion — each lambda
/// gets its own 0-based slot space, and slots that aren't found in the
/// child's dictionary are automatically outer captures.
/// </summary>
public sealed class ExpansionEnvironment {
    private readonly Dictionary<NodeId, int> _slots = new();
    private readonly Dictionary<string, int> _lambdaParameterSlots = new(StringComparer.Ordinal);
    private int _nextSlot;
    private int _nextLambdaIndex;
    private readonly Dictionary<NodeId, LoopBoundary> _loopBoundaries = new();

    // ── Child environments (closure scopes) ─────────────────────

    /// <summary>Parent environment, or null for the root.</summary>
    private readonly ExpansionEnvironment? _parent;

    /// <summary>Creates a root environment.</summary>
    public ExpansionEnvironment() { }

    /// <summary>Creates a child environment for a lambda body.
    /// The child has an independent 0-based slot space.  Slots not
    /// found in the child's dictionary belong to the parent (captures).</summary>
    private ExpansionEnvironment(ExpansionEnvironment parent) {
        _parent = parent;
        // Inherit loop boundaries so break/continue inside a lambda
        // inside a loop resolves correctly.
        _loopBoundaries = parent._loopBoundaries;
        StatementDepth = parent.StatementDepth;
    }

    /// <summary>Create a child environment for a lambda body expansion.
    /// The child gets its own slot space; references to outer-scope slots
    /// become upvalue captures.</summary>
    public ExpansionEnvironment CreateChildScope() => new(this);

    /// <summary>Number of slots allocated in THIS environment (excludes parent).</summary>
    public int LocalSlotCount => _nextSlot;

    /// <summary>Number of slots in the full parent chain (for parameter slot assignment).</summary>
    public int TotalSlotDepth => _parent?.TotalSlotDepth ?? 0;

    /// <summary>Allocate a sequential lambda index for closure call dispatch.</summary>
    public int AllocateLambdaIndex() => _nextLambdaIndex++;

    // ── Slot management ─────────────────────────────────────────

    public bool HasSlot(Node node) => _slots.ContainsKey(node.Id);

    public bool TryGetSlot(Node node, out int slot) =>
        _slots.TryGetValue(node.Id, out slot);

    /// <summary>Returns the existing slot for <paramref name="node"/>,
    /// or assigns and returns a new one.  In a child environment, the
    /// slot is allocated in the child's space (never walks up to parent).</summary>
    public int GetOrAssignSlot(Node node) {
        if (_slots.TryGetValue(node.Id, out var slot))
            return slot;
        slot = _nextSlot++;
        _slots[node.Id] = slot;
        return slot;
    }

    /// <summary>Maps a lambda parameter name to its assigned slot in this child scope.</summary>
    public void RegisterLambdaParameter(string name, int slot) {
        _lambdaParameterSlots[name] = slot;
    }

    /// <summary>Resolves a body parameter by name to a declared lambda parameter slot.</summary>
    public bool TryGetLambdaParameterSlot(string name, out int slot) =>
        _lambdaParameterSlots.TryGetValue(name, out slot);

    /// <summary>Aliases <paramref name="node"/> to an existing slot without advancing <see cref="LocalSlotCount"/>.</summary>
    public void AliasSlot(Node node, int slot) {
        _slots[node.Id] = slot;
    }

    /// <summary>Allocate a temp slot without associating it with a node.</summary>
    public int AllocateTempSlot() => _nextSlot++;

    /// <summary>True if <paramref name="node"/> has a slot in this
    /// environment or any parent.  In child scopes, this detects captures
    /// — if it's true in the parent but not in the child, it's an upvalue.</summary>
    public bool ExistsInScope(Node node) =>
        _slots.ContainsKey(node.Id) || (_parent?.ExistsInScope(node) ?? false);

    /// <summary>Returns the slot index for <paramref name="node"/>,
    /// walking up to parent environments.  The returned value includes
    /// the parent's slot offset.  Used by <c>LoadLocal</c>/<c>StoreLocal</c>
    /// when the variable is in THIS scope (not captured).</summary>
    public bool TryResolveSlot(Node node, out int slot) {
        if (_slots.TryGetValue(node.Id, out slot))
            return true;
        if (_parent is not null)
            return _parent.TryResolveSlot(node, out slot);
        return false;
    }

    // ── Capture detection ───────────────────────────────────────

    /// <summary>Returns true when <paramref name="node"/> is declared
    /// in an ancestor environment (not the current child).</summary>
    public bool IsUpvalue(Node node) =>
        _parent is not null && _parent.ExistsInScope(node) && !_slots.ContainsKey(node.Id);

    /// <summary>Returns the parent-scope slot index for an upvalue node.
    /// The node must be an upvalue (IsUpvalue returns true).</summary>
    public int GetParentSlot(Node node) {
        if (_parent is not null && _parent.TryResolveSlot(node, out var slot))
            return slot;
        throw new InvalidOperationException("Cannot resolve parent slot for non-upvalue node.");
    }

    /// <summary>Upvalue index mapping (child-slots by upvalue sequence).</summary>
    private readonly Dictionary<NodeId, int> _upvalueIndices = new();

    /// <summary>Returns the upvalue index for <paramref name="node"/>,
    /// allocating a new one if needed.  The index is sequential within
    /// this child environment (0, 1, 2, ...).</summary>
    public int GetOrAssignUpvalueIndex(Node node) {
        if (_upvalueIndices.TryGetValue(node.Id, out var idx))
            return idx;
        idx = _upvalueIndices.Count;
        _upvalueIndices[node.Id] = idx;
        return idx;
    }

    /// <summary>Returns all captured node → parent-slot-info pairs in
    /// upvalue index order.  Used by Lambda.ToPrimitives to build the
    /// capture list for AllocClosure.</summary>
    public List<(int ParentSlot, int UpvalueIndex)> GetCaptures() {
        var result = new List<(int ParentSlot, int UpvalueIndex)>(_upvalueIndices.Count);
        foreach (var kv in _upvalueIndices.OrderBy(kv => kv.Value)) {
            var node = kv.Key; // This is NodeId — need the actual slot index
            // We need NodeId → slot index mapping from the parent
            // Store NodeId and resolve later
            if (_parent is not null && _parent.TryResolveSlotByNodeId(kv.Key, out var parentSlot))
                result.Add((parentSlot, kv.Value));
        }
        return result;
    }

    /// <summary>Resolve a slot by NodeId from parent scope. Returns
    /// the slot index assigned in the current or ancestor environment.</summary>
    private bool TryResolveSlotByNodeId(NodeId nodeId, out int slot) {
        if (_slots.TryGetValue(nodeId, out slot))
            return true;
        if (_parent is not null)
            return _parent.TryResolveSlotByNodeId(nodeId, out slot);
        slot = 0;
        return false;
    }

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

    // ── Pending function bodies (lambda closure support) ─────────
    // When a lambda expands, its body is expanded in a child environment.
    // The child produces its own primitive list with 0-based slot indices.
    // ProgramCompiler collects these and compiles them as standalone
    // delegates, with a function table in VmProgram.
    private readonly List<PendingFunction> _pendingFunctions = [];

    /// <summary>Register a lambda body for separate compilation.
    /// <paramref name="body"> is the expanded primitive list from the
    /// child environment (0-based slot indices).
    /// <paramref name="capturedInfo"> maps each child slot index to its
    /// parent (outer-scope) slot index.
    public void AddPendingFunction(int lambdaIndex, List<PrimitiveNode> body,
        IReadOnlyList<(int ChildSlot, int ParentSlot)> capturedInfo,
        int paramCount, int localCount) {
        _pendingFunctions.Add(new PendingFunction(lambdaIndex, body, capturedInfo, paramCount, localCount));
    }

    /// <summary>Returns all pending function bodies and clears the list.</summary>
    public List<PendingFunction> ExtractPendingFunctions() {
        var result = new List<PendingFunction>(_pendingFunctions);
        _pendingFunctions.Clear();
        return result;
    }

    // ── Loop boundary registry (NodeId-keyed) ───────────────────

    /// <summary>
    /// Registers a loop's exit/latch labels keyed by its <see cref="Node.Id"/>.
    /// </summary>
    public void RegisterLoopBoundary(NodeId loopNodeId, LoopBoundary boundary) {
        _loopBoundaries[loopNodeId] = boundary;
    }

    /// <summary>
    /// Retrieves the boundary for the loop identified by <paramref name="loopNodeId"/>.
    /// </summary>
    public LoopBoundary GetLoopBoundary(NodeId loopNodeId) =>
        _loopBoundaries[loopNodeId];
}

/// <summary>A lambda body pending compilation.</summary>
public sealed record PendingFunction(int LambdaIndex, List<PrimitiveNode> Body,
    IReadOnlyList<(int ChildSlot, int ParentSlot)> CapturedInfo,
    int ParamCount, int LocalCount);

/// <summary>Labels for a loop's exit and latch targets.</summary>
public sealed record LoopBoundary(Label Exit, Label Latch);