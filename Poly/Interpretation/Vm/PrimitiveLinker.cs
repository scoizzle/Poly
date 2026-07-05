using Poly.Syntax.Primitives;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Links a sequence of <see cref="PrimitiveNode"/> instances by resolving
/// <see cref="Label"/> references to absolute PC offsets.
/// </summary>
internal static class PrimitiveLinker {
    /// <summary>Links the expanded primitives into a flat array with resolved branch targets.</summary>
    /// <param name="primitives">The expanded primitive sequence (may contain Label markers).</param>
    /// <returns>A flat PrimitiveNode array. Label markers are kept in-place (as no-ops)
    /// so branch targets always point to a valid position in the array.</returns>
    public static IReadOnlyList<PrimitiveNode> Link(IEnumerable<PrimitiveNode> primitives) {
        var flat = primitives.ToList();

        // Pass 1: build label → index mapping
        var labelToIdx = new Dictionary<Label, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < flat.Count; i++) {
            if (flat[i] is Label label && !labelToIdx.ContainsKey(label))
                labelToIdx[label] = i;
        }

        // Pass 2: replace Goto/CondGoto label refs with resolved position references.
        // Label markers stay in the sequence so forward/backward branches always
        // point to a valid index.
        var result = new List<PrimitiveNode>(flat.Count);
        for (int i = 0; i < flat.Count; i++) {
            var prim = flat[i];

            if (prim is Label) {
                result.Add(prim);
            }
            else if (prim is Goto g) {
                result.Add(new ResolvedGoto(labelToIdx[g.Target]) { Id = prim.Id });
            }
            else if (prim is CondGoto cg) {
                result.Add(new ResolvedCondGoto(labelToIdx[cg.Target]) { Id = prim.Id });
            }
            else {
                result.Add(prim);
            }
        }

        return result.AsReadOnly();
    }
}

// ── Resolved variants (label → PC offset) ─────────────────────────

/// <summary>Resolved unconditional jump with absolute PC target.</summary>
internal sealed record ResolvedGoto(int TargetPc) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (0, 0);
}

/// <summary>Resolved conditional jump with absolute PC target.</summary>
internal sealed record ResolvedCondGoto(int TargetPc) : PrimitiveNode {
    public override (int Pop, int Push) StackEffect => (1, 0);
}