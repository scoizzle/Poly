using Poly.Syntax.Nodes;

namespace Poly.Syntax.Primitives;

/// <summary>
/// Facade that runs the multi-pass reconstruction pipeline to reconstruct
/// statement-level and control-flow <see cref="Node"/> trees
/// from <see cref="PrimitiveNode"/> sequences.
///
/// The pipeline runs passes in order: CFG Building → Loop Recognition →
/// Conditional Recognition → Switch Recognition → Jump Target Recognition →
/// Expression Reconstruction → Statement Assembly.
///
/// Each pass produces metadata consumed by later passes, enabling increasingly
/// sophisticated analysis without a single monolithic pattern-matcher.
/// </summary>
internal sealed class StatementReconstructor {
    private readonly ReconstructionPipeline _pipeline;
    private readonly ReconstructionContext _context;

    public StatementReconstructor(
        SlotAnalyzer slotAnalyzer,
        PrimitiveReconstructorSettings settings,
        AnalysisContext? context) {
        var builder = new ReconstructionPipelineBuilder()
            .Add(new CfgBuildingPass())
            .Add(new LoopRecognitionPass())
            .Add(new ConditionalRecognitionPass())
            .Add(new SwitchRecognitionPass())
            .Add(new JumpTargetRecognitionPass())
            .Add(new ExpressionReconstructionPass())
            .Add(new StatementAssemblyPass());

        _pipeline = builder.Build();
        _context = new ReconstructionContext {
            SlotAnalyzer = slotAnalyzer,
            OuterContext = context,
            Settings = settings
        };
    }

    /// <summary>
    /// Try to reconstruct a node from the primitive sequence starting at <paramref name="startIndex"/>.
    /// Runs the full pipeline on the provided primitives.
    /// </summary>
    public bool TryReconstruct(
        IReadOnlyList<PrimitiveNode> primitives,
        int startIndex,
        out Node? result,
        out int consumed) {
        // Run the full pipeline
        _pipeline.Execute(primitives, _context);

        result = _context.ReconstructedRoot;
        consumed = primitives.Count;
        return result is not null;
    }
}

/// <summary>
/// Pass 4: Switch recognition — placeholder for future implementation.
/// </summary>
internal sealed class SwitchRecognitionPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.SwitchRecognition;
    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) { }
}

/// <summary>
/// Pass 5: Jump target recognition — placeholder for future implementation.
/// Pre-computes return/throw positions and break/continue targets.
/// </summary>
internal sealed class JumpTargetRecognitionPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.JumpTargetRecognition;
    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) { }
}

/// <summary>
/// Pass 6: Expression reconstruction — processes basic blocks through
/// the ExpressionReconstructor. Results are consumed by StatementAssemblyPass.
/// </summary>
internal sealed class ExpressionReconstructionPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.ExpressionReconstruction;
    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) { }
}