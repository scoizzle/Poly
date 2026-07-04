namespace Poly.Syntax.Primitives;

using static ReconstructionHelpers;

/// <summary>
/// Pass 1: Builds a control-flow graph by scanning for Label and Goto/CondGoto primitives.
/// Produces basic block boundaries.
/// </summary>
internal sealed class CfgBuildingPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.CfgBuilding;

    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) {
        var labelPositions = new Dictionary<string, int>();

        // Find all label positions
        for (int i = 0; i < primitives.Count; i++) {
            if (primitives[i] is Label l) {
                labelPositions[l.Name ?? ""] = i;
            }
        }

        // Build basic blocks: block boundaries are labels and goto/condgoto targets
        var boundaries = new HashSet<int> { 0, primitives.Count };
        for (int i = 0; i < primitives.Count; i++) {
            if (primitives[i] is Label) {
                boundaries.Add(i);
                boundaries.Add(i + 1);
            }
            if (primitives[i] is Goto g && g.Target is Label gt
                && labelPositions.TryGetValue(gt.Name ?? "", out var gotoPos)) {
                boundaries.Add(gotoPos);
                boundaries.Add(i + 1);
            }
            if (primitives[i] is CondGoto cg && cg.Target is Label ct
                && labelPositions.TryGetValue(ct.Name ?? "", out var condGotoPos)) {
                boundaries.Add(condGotoPos);
                boundaries.Add(i + 1);
            }
        }

        // Sort and form blocks
        var sorted = boundaries.OrderBy(x => x).Distinct().ToList();
        var blocks = new List<(int Start, int End)>();
        for (int i = 0; i < sorted.Count - 1; i++) {
            if (sorted[i] < sorted[i + 1]) {
                blocks.Add((sorted[i], sorted[i + 1]));
            }
        }

        context.BasicBlocks = blocks;
    }
}