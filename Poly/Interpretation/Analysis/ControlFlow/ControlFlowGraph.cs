namespace Poly.Interpretation.Analysis.ControlFlow;

/// <summary>
/// Represents a control flow graph for an AST.
/// The CFG consists of basic blocks connected by edges representing possible control flow.
/// </summary>
public sealed class ControlFlowGraph {
    private readonly List<BasicBlock> _blocks = [];
    private readonly Dictionary<Node, BasicBlock> _nodeToBlock = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets the entry block of the CFG.
    /// </summary>
    public BasicBlock Entry => _blocks[0];

    /// <summary>
    /// Gets all basic blocks in the CFG.
    /// </summary>
    public IReadOnlyList<BasicBlock> Blocks => _blocks;

    /// <summary>
    /// Gets the exit blocks of the CFG.
    /// </summary>
    public IEnumerable<BasicBlock> ExitBlocks => _blocks.Where(b => b.IsExit);

    /// <summary>
    /// Gets the unreachable blocks in the CFG.
    /// </summary>
    public IEnumerable<BasicBlock> UnreachableBlocks => _blocks.Where(b => !b.IsReachable);

    /// <summary>
    /// Gets blocks that contain dead code (unreachable statements).
    /// </summary>
    public IEnumerable<Node> DeadCode {
        get {
            foreach (var block in UnreachableBlocks) {
                foreach (var statement in block.Statements) {
                    yield return statement;
                }
            }
        }
    }

    /// <summary>
    /// Creates a new basic block and adds it to the CFG.
    /// </summary>
    internal BasicBlock CreateBlock() {
        var block = new BasicBlock(_blocks.Count);
        _blocks.Add(block);
        return block;
    }

    /// <summary>
    /// Associates a node with its containing basic block.
    /// </summary>
    internal void MapNodeToBlock(Node node, BasicBlock block) {
        _nodeToBlock[node] = block;
    }

    /// <summary>
    /// Gets the basic block containing the specified node.
    /// </summary>
    public BasicBlock? GetBlockForNode(Node node) {
        return _nodeToBlock.TryGetValue(node, out var block) ? block : null;
    }

    /// <summary>
    /// Performs reachability analysis to mark reachable blocks.
    /// </summary>
    internal void ComputeReachability() {
        if (_blocks.Count == 0) return;

        var visited = new HashSet<BasicBlock>();
        var worklist = new Queue<BasicBlock>();

        // Start from entry block
        worklist.Enqueue(Entry);
        Entry.IsReachable = true;

        while (worklist.Count > 0) {
            var current = worklist.Dequeue();
            if (visited.Contains(current)) continue;
            visited.Add(current);

            foreach (var successor in current.Successors) {
                successor.IsReachable = true;
                worklist.Enqueue(successor);
            }
        }
    }

    /// <summary>
    /// Marks exit blocks based on blocks with no successors or containing return/throw.
    /// </summary>
    internal void IdentifyExitBlocks() {
        foreach (var block in _blocks) {
            if (block.Successors.Count == 0) {
                block.IsExit = true;
            }
        }
    }

    public override string ToString() =>
        $"CFG: {_blocks.Count} blocks, {UnreachableBlocks.Count()} unreachable";
}