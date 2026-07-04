namespace Poly.Syntax.Primitives;

/// <summary>
/// Shared helper methods used by multiple reconstruction passes.
/// </summary>
internal static class ReconstructionHelpers {

    public static int FindLabel(IReadOnlyList<PrimitiveNode> primitives, int fromIndex, string name) {
        for (int i = fromIndex; i < primitives.Count; i++) {
            if (primitives[i] is Label { Name: var n } && n == name)
                return i;
        }
        return -1;
    }

    public static int FindCondGoto(IReadOnlyList<PrimitiveNode> primitives, int fromIndex, out string? targetLabel) {
        for (int i = fromIndex; i < primitives.Count; i++) {
            if (primitives[i] is CondGoto cg && cg.Target is Label target) {
                targetLabel = target.Name;
                return i;
            }
        }
        targetLabel = null;
        return -1;
    }

    public static int FindGoto(IReadOnlyList<PrimitiveNode> primitives, int fromIndex, out string? targetLabel) {
        for (int i = fromIndex; i < primitives.Count; i++) {
            if (primitives[i] is Goto g && g.Target is Label target) {
                targetLabel = target.Name;
                return i;
            }
        }
        targetLabel = null;
        return -1;
    }

    public static IReadOnlyList<PrimitiveNode> Slice(
        IReadOnlyList<PrimitiveNode> primitives, int start, int endExclusive) {
        if (start >= endExclusive || start >= primitives.Count)
            return Array.Empty<PrimitiveNode>();
        endExclusive = Math.Min(endExclusive, primitives.Count);
        var result = new PrimitiveNode[endExclusive - start];
        for (int i = start; i < endExclusive; i++) {
            result[i - start] = primitives[i];
        }
        return result;
    }

    /// <summary>
    /// Returns true if the label name is one used structurally by the lowering passes
    /// and should be skipped during reconstruction.
    /// </summary>
    public static bool IsStructuralLabel(string name) {
        return name.StartsWith("while_")
            || name.StartsWith("dowhile_")
            || name.StartsWith("for_")
            || name.StartsWith("ternary_")
            || name.StartsWith("coalesce_")
            || name.StartsWith("switch_")
            || name == "else"
            || name == "merge"
            || name == "case";
    }
}