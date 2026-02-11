namespace Poly.Interpretation.Analysis.ControlFlow;

/// <summary>
/// Represents a basic block in a control flow graph.
/// A basic block is a sequence of statements with single entry and exit points.
/// </summary>
public sealed class BasicBlock {
    private readonly List<BasicBlock> _predecessors = [];
    private readonly List<BasicBlock> _successors = [];
    private readonly List<Node> _statements = [];

    /// <summary>
    /// Unique identifier for this basic block.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the statements contained in this basic block.
    /// </summary>
    public IReadOnlyList<Node> Statements => _statements;

    /// <summary>
    /// Gets the blocks that can transfer control to this block.
    /// </summary>
    public IReadOnlyList<BasicBlock> Predecessors => _predecessors;

    /// <summary>
    /// Gets the blocks that can receive control from this block.
    /// </summary>
    public IReadOnlyList<BasicBlock> Successors => _successors;

    /// <summary>
    /// Gets whether this is the entry block of the CFG.
    /// </summary>
    public bool IsEntry => Predecessors.Count == 0 && Id == 0;

    /// <summary>
    /// Gets whether this is an exit block of the CFG.
    /// </summary>
    public bool IsExit { get; internal set; }

    /// <summary>
    /// Gets whether this block is reachable from the entry block.
    /// </summary>
    public bool IsReachable { get; internal set; }

    /// <summary>
    /// Gets the terminator statement (if any) that ends this block.
    /// This could be a return, break, continue, goto, or throw statement.
    /// </summary>
    public Node? Terminator { get; private set; }

    public BasicBlock(int id)
    {
        Id = id;
        IsReachable = id == 0; // Entry block is always reachable initially
    }

    internal void AddStatement(Node statement)
    {
        _statements.Add(statement);
    }

    internal void SetTerminator(Node terminator)
    {
        Terminator = terminator;
    }

    internal void AddPredecessor(BasicBlock block)
    {
        if (!_predecessors.Contains(block)) {
            _predecessors.Add(block);
        }
    }

    internal void AddSuccessor(BasicBlock block)
    {
        if (!_successors.Contains(block)) {
            _successors.Add(block);
            block.AddPredecessor(this);
        }
    }

    public override string ToString()
    {
        var kind = IsEntry ? "Entry" : (IsExit ? "Exit" : "Block");
        return $"{kind}[{Id}] ({Statements.Count} statements, {Successors.Count} successors)";
    }
}