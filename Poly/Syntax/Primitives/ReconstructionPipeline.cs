namespace Poly.Syntax.Primitives;

/// <summary>
/// Phase identifier for the multi-pass reconstruction pipeline.
/// Passes run in order; later passes consume metadata from earlier ones.
/// </summary>
internal enum ReconstructionPhase {
    /// <summary>Build a control-flow graph from labels and branches.</summary>
    CfgBuilding,
    /// <summary>Identify natural loops (back-edges in the CFG).</summary>
    LoopRecognition,
    /// <summary>Identify if/then/else and ternary conditional patterns.</summary>
    ConditionalRecognition,
    /// <summary>Identify switch/case patterns.</summary>
    SwitchRecognition,
    /// <summary>Identify return, throw, break, continue, goto/label.</summary>
    JumpTargetRecognition,
    /// <summary>Reconstruct expression trees within basic blocks.</summary>
    ExpressionReconstruction,
    /// <summary>Final assembly: combine all recognized patterns into a statement tree.</summary>
    StatementAssembly
}

/// <summary>
/// Base interface for a single pass in the reconstruction pipeline.
/// Each pass analyzes primitives and stores metadata, or transforms
/// a <see cref="ReconstructionContext"/>.
/// </summary>
internal interface IReconstructionPass {
    ReconstructionPhase Phase { get; }
    void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context);
}

/// <summary>
/// Shared context that flows through all reconstruction passes.
/// Passes store their findings here for consumption by later passes.
/// </summary>
internal sealed class ReconstructionContext {
    /// <summary>
    /// Basic block boundaries detected by the CFG pass.
    /// Each block is (startIndex, endExclusive).
    /// </summary>
    public List<(int Start, int End)>? BasicBlocks { get; set; }

    /// <summary>
    /// Metadata about recognized loops, populated by the loop pass.
    /// </summary>
    public List<LoopInfo>? Loops { get; set; }

    /// <summary>
    /// Metadata about recognized conditionals, populated by the conditional pass.
    /// </summary>
    public List<ConditionalInfo>? Conditionals { get; set; }

    /// <summary>
    /// The final reconstructed node tree, populated by the statement assembly pass.
    /// </summary>
    public Node? ReconstructedRoot { get; set; }

    /// <summary>
    /// The slot analyzer (shared across passes).
    /// </summary>
    public required SlotAnalyzer SlotAnalyzer { get; init; }

    /// <summary>
    /// The optional analysis context from the outer pipeline.
    /// </summary>
    public AnalysisContext? OuterContext { get; init; }

    /// <summary>
    /// Settings controlling reconstruction behaviour.
    /// </summary>
    public required PrimitiveReconstructorSettings Settings { get; init; }
}

/// <summary>Information about a recognized loop construct.</summary>
internal sealed record LoopInfo(
    int HeaderIndex,
    int CondGotoIndex,
    int BodyStart,
    int BodyEnd,
    int GotoIndex,
    int ExitIndex,
    string LoopKind  // "while", "dowhile", "for"
);

/// <summary>Information about a recognized conditional (if or ternary).</summary>
internal sealed record ConditionalInfo(
    int CondGotoIndex,
    int ThenStart,
    int ThenEnd,
    int ElseLabelIndex,
    int ElseEnd,
    int MergeLabelIndex,
    int? LoadLocalIndex,
    string Kind // "if" or "ternary"
);

/// <summary>
/// Builder for the multi-pass reconstruction pipeline.
/// Passes run in the order they are added (sorted by Phase).
/// </summary>
internal sealed class ReconstructionPipelineBuilder {
    private readonly List<IReconstructionPass> _passes = new();

    public ReconstructionPipelineBuilder Add(IReconstructionPass pass) {
        _passes.Add(pass);
        return this;
    }

    public ReconstructionPipeline Build() {
        return new ReconstructionPipeline(
            _passes.OrderBy(p => p.Phase).ToList()
        );
    }
}

/// <summary>
/// Executes the reconstruction pipeline: runs each pass in order,
/// passing the shared context between them.
/// </summary>
internal sealed class ReconstructionPipeline {
    private readonly IReadOnlyList<IReconstructionPass> _passes;

    public ReconstructionPipeline(IReadOnlyList<IReconstructionPass> passes) {
        _passes = passes;
    }

    public void Execute(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) {
        foreach (var pass in _passes) {
            pass.Run(primitives, context);
        }
    }
}