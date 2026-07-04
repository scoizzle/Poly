namespace Poly.Syntax.Primitives;

using static ReconstructionHelpers;

/// <summary>
/// Pass 2: Recognizes loop structures (while, do-while, for) by matching
/// label name conventions and structural patterns in the primitive array.
/// </summary>
internal sealed class LoopRecognitionPass : IReconstructionPass {
    public ReconstructionPhase Phase => ReconstructionPhase.LoopRecognition;

    public void Run(IReadOnlyList<PrimitiveNode> primitives, ReconstructionContext context) {
        var loops = new List<LoopInfo>();

        for (int i = 0; i < primitives.Count; i++) {
            if (primitives[i] is Label { Name: var name }) {
                switch (name) {
                    case "while_header" when TryMatchWhile(primitives, i, out var loop):
                        loops.Add(loop);
                        break;
                    case "dowhile_body" when TryMatchDoWhile(primitives, i, out var loop):
                        loops.Add(loop);
                        break;
                    case "for_header" when TryMatchFor(primitives, i, out var loop):
                        loops.Add(loop);
                        break;
                }
            }
        }

        context.Loops = loops.Count > 0 ? loops : null;
    }

    private static bool TryMatchWhile(IReadOnlyList<PrimitiveNode> primitives, int headerIdx, out LoopInfo loop) {
        loop = default!;

        int condGotoIdx = FindCondGoto(primitives, headerIdx + 1, out _);
        if (condGotoIdx < 0) return false;

        int gotoIdx = FindGoto(primitives, condGotoIdx + 1, out var gotoName);
        if (gotoIdx < 0 || gotoName != "while_header") return false;

        int exitIdx = FindLabel(primitives, gotoIdx + 1, "while_exit");
        if (exitIdx < 0) return false;

        int bodyStart = condGotoIdx + 1;
        if (bodyStart < primitives.Count && primitives[bodyStart] is Label { Name: "while_body" })
            bodyStart++;

        int bodyEnd = gotoIdx;
        while (bodyEnd > bodyStart && primitives[bodyEnd - 1] is Discard)
            bodyEnd--;

        loop = new LoopInfo(headerIdx, condGotoIdx, bodyStart, bodyEnd, gotoIdx, exitIdx, "while");
        return true;
    }

    private static bool TryMatchDoWhile(IReadOnlyList<PrimitiveNode> primitives, int bodyLabelIdx, out LoopInfo loop) {
        loop = default!;

        int condLabelIdx = FindLabel(primitives, bodyLabelIdx + 1, "dowhile_cond");
        if (condLabelIdx < 0) return false;

        int condGotoIdx = FindCondGoto(primitives, condLabelIdx + 1, out _);
        if (condGotoIdx < 0) return false;

        int gotoIdx = FindGoto(primitives, condGotoIdx + 1, out var gotoName);
        if (gotoIdx < 0 || gotoName != "dowhile_body") return false;

        int exitIdx = FindLabel(primitives, gotoIdx + 1, "dowhile_exit");
        if (exitIdx < 0) return false;

        int bodyEnd = condLabelIdx;
        if (bodyEnd > bodyLabelIdx + 1 && primitives[bodyEnd - 1] is Discard)
            bodyEnd--;

        loop = new LoopInfo(bodyLabelIdx, condGotoIdx, bodyLabelIdx + 1, bodyEnd, gotoIdx, exitIdx, "dowhile");
        return true;
    }

    private static bool TryMatchFor(IReadOnlyList<PrimitiveNode> primitives, int headerIdx, out LoopInfo loop) {
        loop = default!;

        int condGotoIdx = FindCondGoto(primitives, headerIdx + 1, out _);
        if (condGotoIdx < 0) return false;

        int bodyStart = condGotoIdx + 1;
        if (bodyStart < primitives.Count && primitives[bodyStart] is Label { Name: "for_body" })
            bodyStart++;

        int gotoIdx = FindGoto(primitives, bodyStart, out var gotoName);
        if (gotoIdx < 0 || gotoName != "for_header") return false;

        int exitIdx = FindLabel(primitives, gotoIdx + 1, "for_exit");
        if (exitIdx < 0) return false;

        int bodyEnd = gotoIdx;
        if (bodyEnd > bodyStart && primitives[bodyEnd - 1] is Discard) {
            bodyEnd--;
            // Check for two discards (body + incr)
            int discardsFound = 0;
            for (int scan = gotoIdx - 1; scan >= bodyStart; scan--) {
                if (primitives[scan] is Discard) {
                    discardsFound++;
                    if (discardsFound == 2) { bodyEnd = scan; break; }
                }
            }
        }

        while (bodyEnd > bodyStart && primitives[bodyEnd - 1] is Discard)
            bodyEnd--;

        loop = new LoopInfo(headerIdx, condGotoIdx, bodyStart, bodyEnd, gotoIdx, exitIdx, "for");
        return true;
    }
}