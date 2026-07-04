namespace Poly.Syntax.Primitives;

using static ReconstructionHelpers;

/// <summary>
/// Pass 3: Recognizes if/then/else and ternary conditional patterns by matching
/// CondGoto → Goto → Label → Label structural signatures.
/// </summary>
internal sealed class ConditionalRecognitionPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.ConditionalRecognition;

    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) {
        var conditionals = new List<ConditionalInfo>();

        for (int i = 0; i < primitives.Count; i++) {
            if (TryMatchTernary(primitives, i, out var info)) {
                conditionals.Add(info);
                i = info.MergeLabelIndex;
            }
            else if (TryMatchIf(primitives, i, out info)) {
                conditionals.Add(info);
                i = info.MergeLabelIndex;
            }
        }

        context.Conditionals = conditionals.Count > 0 ? conditionals : null;
    }

    /// <summary>
    /// Ternary pattern:
    ///   [condition] CondGoto("ternary_else") [then] StoreLocal(t) Goto("ternary_merge")
    ///   Label("ternary_else") [else] StoreLocal(t) Label("ternary_merge") LoadLocal(t)
    /// </summary>
    private static bool TryMatchTernary(IReadOnlyList<PrimitiveNode> primitives, int startIndex, out ConditionalInfo info) {
        info = default!;

        int condGotoIdx = FindCondGoto(primitives, startIndex, out var target);
        if (condGotoIdx < 0 || target != "ternary_else") return false;

        int storeLocal1 = -1;
        for (int i = condGotoIdx + 1; i < primitives.Count; i++) {
            if (primitives[i] is StoreLocal) { storeLocal1 = i; break; }
        }
        if (storeLocal1 < 0) return false;

        int gotoIdx = -1;
        for (int i = storeLocal1 + 1; i < primitives.Count; i++) {
            if (primitives[i] is Goto g && g.Target is Label { Name: "ternary_merge" }) { gotoIdx = i; break; }
        }
        if (gotoIdx < 0) return false;

        int elseLabelIdx = FindLabel(primitives, gotoIdx + 1, "ternary_else");
        if (elseLabelIdx < 0) return false;

        int storeLocal2 = -1;
        for (int i = elseLabelIdx + 1; i < primitives.Count; i++) {
            if (primitives[i] is StoreLocal) { storeLocal2 = i; break; }
        }
        if (storeLocal2 < 0) return false;

        int mergeLabelIdx = FindLabel(primitives, storeLocal2 + 1, "ternary_merge");
        if (mergeLabelIdx < 0) return false;

        int loadLocalIdx = -1;
        for (int i = mergeLabelIdx + 1; i < primitives.Count; i++) {
            if (primitives[i] is LoadLocal) { loadLocalIdx = i; break; }
        }
        if (loadLocalIdx < 0) return false;

        info = new ConditionalInfo(condGotoIdx, condGotoIdx + 1, storeLocal1,
            elseLabelIdx, storeLocal2, mergeLabelIdx, loadLocalIdx, "ternary");
        return true;
    }

    /// <summary>
    /// If-statement pattern (statement context):
    ///   [condition] CondGoto("else") [then] Goto("merge") Label("else") [else?] Label("merge")
    /// </summary>
    private static bool TryMatchIf(IReadOnlyList<PrimitiveNode> primitives, int startIndex, out ConditionalInfo info) {
        info = default!;

        int condGotoIdx = FindCondGoto(primitives, startIndex, out var target);
        if (condGotoIdx < 0 || target != "else") return false;

        int gotoIdx = -1;
        for (int i = condGotoIdx + 1; i < primitives.Count; i++) {
            if (primitives[i] is Goto g && g.Target is Label { Name: "merge" }) { gotoIdx = i; break; }
        }
        if (gotoIdx < 0) return false;

        int elseLabelIdx = FindLabel(primitives, gotoIdx + 1, "else");
        if (elseLabelIdx < 0) return false;

        int mergeLabelIdx = FindLabel(primitives, elseLabelIdx + 1, "merge");
        if (mergeLabelIdx < 0) return false;

        info = new ConditionalInfo(condGotoIdx, condGotoIdx + 1, gotoIdx,
            elseLabelIdx, mergeLabelIdx, mergeLabelIdx, null, "if");
        return true;
    }
}