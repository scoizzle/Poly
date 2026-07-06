using Poly.Syntax.Primitives;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Computes ring-based µop value allocation for a linked primitive sequence.
///
/// The ring allocator simulates the eval-stack at compile time, assigning each
/// producer µop a ring slot index equal to its depth in the evaluation stack.
/// This keeps the number of LINQ local variables bounded (typically ~10–20)
/// regardless of total µop count, because slots are recycled as values are
/// consumed.
///
/// Branch-aware: when a <c>CondGoto</c> or <c>Goto</c> targets a label, the
/// ring depth at that label is restored to what the predecessor expects — not
/// the linear fallthrough depth. This lets both arms of a branch leave values
/// at the same ring depth for <c>Phi</c> to merge.
/// </summary>
public sealed record RingAllocation {
    /// <summary>Maps producer µop PC → ring slot index.</summary>
    public required IReadOnlyDictionary<int, int> ProducerToRingIdx { get; init; }

    /// <summary>Maps each µop PC → the eval-stack depth at entry to that µop.</summary>
    public required IReadOnlyDictionary<int, int> RingDepthAtPC { get; init; }

    /// <summary>For each µop, the list of µop PCs that produced its consumed values.</summary>
    public required IReadOnlyList<int[]> ConsumedPcs { get; init; }

    /// <summary>Maximum number of ring slots needed (i.e. max concurrent live values).</summary>
    public int MaxDepth { get; init; }

    /// <summary>Produce a <see cref="PcToRingDepth"/> side-table for debug/EH stack reconstruction.</summary>
    public PcToRingDepth ToSideTable() => new(RingDepthAtPC);

    /// <summary>
    /// Compute ring allocation for a linked primitive sequence in a single pass.
    /// The sequence should already have been processed by <see cref="PrimitiveLinker.Link"/>
    /// so that <c>Label</c> references are resolved to <c>ResolvedGoto</c>/<c>ResolvedCondGoto</c>.
    /// </summary>
    public static RingAllocation Compute(IReadOnlyList<PrimitiveNode> primitives) {
        // Pre-compute expected depths at branch targets 🪛
        var targetDepth = BuildTargetDepth(primitives);

        var ring = new List<int>();
        var producerToRingIdx = new Dictionary<int, int>();
        var ringDepthAtPC = new Dictionary<int, int>();
        var consumedPcs = new int[primitives.Count][];

        for (int pc = 0; pc < primitives.Count; pc++) {
            // At a branch-target label, restore ring to expected predecessor depth
            if (targetDepth.TryGetValue(pc, out int expectDepth) && expectDepth < ring.Count)
                ring.RemoveRange(expectDepth, ring.Count - expectDepth);

            var (pop, push) = primitives[pc].StackEffect;
            int entryDepth = ring.Count;
            ringDepthAtPC[pc] = entryDepth;

            // Compute consumed PCs (backward-scan through the ring)
            int toPop = Math.Min(pop, entryDepth);
            var consumed = new int[toPop];
            for (int i = 0; i < toPop; i++)
                consumed[toPop - 1 - i] = ring[entryDepth - 1 - i];
            consumedPcs[pc] = consumed;

            // Pop consumed values from the ring
            for (int i = 0; i < toPop && ring.Count > 0; i++)
                ring.RemoveAt(ring.Count - 1);

            // Push produced value onto the ring
            if (push > 0) {
                producerToRingIdx[pc] = entryDepth - toPop;
                for (int i = 0; i < push; i++)
                    ring.Add(pc);
            }
        }

        int maxDepth = producerToRingIdx.Count > 0
            ? producerToRingIdx.Values.Max() + 1
            : 0;

        return new RingAllocation {
            ProducerToRingIdx = producerToRingIdx,
            RingDepthAtPC = ringDepthAtPC,
            ConsumedPcs = consumedPcs,
            MaxDepth = maxDepth
        };
    }

    /// <summary>
    /// Build a map from branch-target PC → expected ring depth.
    /// For <c>ResolvedCondGoto</c>: the depth after popping the condition (same as fallthrough).
    /// For <c>ResolvedGoto</c>: the depth at the Goto site (no stack effect).
    ///
    /// <b>Note (K-034):</b> Only the first predecessor's depth is stored per target.
    /// There is no convergence validation — this is a known gap.
    /// </summary>
    private static Dictionary<int, int> BuildTargetDepth(IReadOnlyList<PrimitiveNode> primitives) {
        var result = new Dictionary<int, int>();
        var sim = new List<int>();
        for (int pc = 0; pc < primitives.Count; pc++) {
            var (pop, push) = primitives[pc].StackEffect;
            int toPop = Math.Min(pop, sim.Count);
            if (toPop > 0) sim.RemoveRange(sim.Count - toPop, toPop);
            int afterDepth = sim.Count;
            for (int j = 0; j < push; j++) sim.Add(pc);

            if (primitives[pc] is ResolvedCondGoto cg) {
                if (!result.ContainsKey(cg.TargetPc))
                    result[cg.TargetPc] = afterDepth;
            }
            if (primitives[pc] is ResolvedGoto g) {
                if (!result.ContainsKey(g.TargetPc))
                    result[g.TargetPc] = afterDepth;
            }
        }
        return result;
    }
}