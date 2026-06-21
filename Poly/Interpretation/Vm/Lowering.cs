using Poly.Interpretation.Analysis.LoweringPrep;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm.Instructions;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Vm;

/// <summary>Pure AST → µop transformation. Requires analysis metadata from
/// LoweringPrep and UopGeneration passes (no legacy fallback).</summary>
public static class Lowering {
    public static LoweringResult Lower(Node node, AnalysisResult analysis) {
        var rootUops = analysis.GetMetadata<LoweredUopMetadata>(node)?.Uops;
        if (rootUops is null)
            throw new InvalidOperationException(
                "Missing LoweredUopMetadata — register UseLoweringPreparation() and UseUopGeneration() on the AnalyzerBuilder.");
        return Assemble(node, analysis, rootUops);
    }

    private static LoweringResult Assemble(Node root, AnalysisResult analysis, List<Instruction> rootUops) {
        var result = new List<Instruction>();
        var labelPositions = new Dictionary<int, int>();
        var labelRefs = new List<(int Index, int LabelId)>();
        var ring = new List<int>();
        var labelRings = new Dictionary<int, List<int>>();

        void EmitFragment(List<Instruction> fragment) {
            for (int fi = 0; fi < fragment.Count; fi++) {
                var inst = fragment[fi];

                if (inst is LoadSlot && fi + 1 < fragment.Count && fragment[fi + 1] is PopOp) {
                    fi++;
                    continue;
                }

                if (inst is LabelMarker lm) {
                    labelPositions[lm.LabelId] = result.Count;
                    labelRings[lm.LabelId] = new List<int>(ring);
                    continue;
                }

                var consumed = new int[inst.PopCount];
                int entryDepth = ring.Count;
                int toPop = Math.Min(inst.PopCount, entryDepth);
                for (int i = 0; i < toPop; i++)
                    consumed[inst.PopCount - 1 - i] = ring[entryDepth - 1 - i];

                if (inst is BranchIfFalse bif)
                    labelRefs.Add((result.Count, bif.Target));
                else if (inst is Jump jmp)
                    labelRefs.Add((result.Count, jmp.Target));

                int idx = result.Count;
                result.Add(inst with {
                    ConsumedFromPcs = consumed.Length > 0 ? consumed : null
                });

                for (int i = 0; i < toPop && ring.Count > 0; i++)
                    ring.RemoveAt(ring.Count - 1);
                for (int i = 0; i < inst.PushCount; i++)
                    ring.Add(idx);
            }
        }

        var sourceRanges = new Dictionary<NodeId, SourceRange>();

        void EmitFragmentWithRanges(List<Instruction> fragment) {
            int before = result.Count;
            EmitFragment(fragment);
            int after = result.Count;
            if (before == after) return;

            var seenNodes = new HashSet<NodeId>();
            for (int i = before; i < after; i++) {
                var src = result[i].SourceNodeId;
                if (src is null || !seenNodes.Add(src.Value)) continue;
                int last = i;
                for (int j = i + 1; j < after; j++)
                    if (result[j].SourceNodeId == src) last = j;
                if (sourceRanges.TryGetValue(src.Value, out var existing)) {
                    sourceRanges[src.Value] = new SourceRange(root,
                        Math.Min(existing.FirstProgramCounter, i),
                        Math.Max(existing.LastProgramCounterInclusive, last));
                }
                else {
                    sourceRanges[src.Value] = new SourceRange(root, i, last);
                }
            }
        }

        EmitFragmentWithRanges(rootUops);

        if (result.Count == 0 || result[^1] is not (ReturnOp or ReturnFromCall)) {
            var consumed = ring.Count > 0 ? new[] { ring[^1] } : null;
            result.Add(new ReturnOp { SourceNodeId = root.Id, ConsumedFromPcs = consumed });
        }

        // ── φ detection ──
        var sortedLabels = labelRings
            .Select(kv => (Label: kv.Key, Pos: labelPositions.GetValueOrDefault(kv.Key, -1), Ring: kv.Value))
            .Where(x => x.Pos >= 0)
            .OrderBy(x => x.Pos)
            .ToList();

        for (int li = 1; li < sortedLabels.Count; li++) {
            var prev = sortedLabels[li - 1];
            var curr = sortedLabels[li];

            int maxRingDepth = Math.Max(prev.Ring.Count, curr.Ring.Count);
            int diffDepth = -1;
            for (int offset = 0; offset < maxRingDepth; offset++) {
                int pVal = (offset < prev.Ring.Count) ? prev.Ring[prev.Ring.Count - 1 - offset] : -1;
                int cVal = (offset < curr.Ring.Count) ? curr.Ring[curr.Ring.Count - 1 - offset] : -1;
                if (pVal != cVal && pVal >= 0 && cVal >= 0) { diffDepth = offset; break; }
            }
            if (diffDepth < 0) continue;

            int sourcePc = -1;
            for (int j = Math.Min(curr.Pos, result.Count - 1); j >= 0; j--) {
                if (result[j] is Instruction instr) {
                    var t = instr switch {
                        var x when x.GetType().Name == "Jump" => (int)((dynamic)x).Target,
                        _ => -1
                    };
                    if (t == curr.Label) { sourcePc = j; break; }
                }
            }
            if (sourcePc < 0) continue;

            bool hasBranchTarget = false;
            for (int k = 0; k < result.Count && !hasBranchTarget; k++)
                if (result[k] is Instruction instr2 && instr2.GetType().Name == "BranchIfFalse")
                    if ((int)((dynamic)instr2).Target == prev.Label) hasBranchTarget = true;
            if (!hasBranchTarget) continue;

            for (int j = curr.Pos; j < result.Count; j++) {
                if (result[j].PopCount > 0 && result[j] is not LabelMarker) {
                    var old = result[j];
                    var consumed = old.ConsumedFromPcs;
                    if (consumed is not null && consumed.Length > 0) {
                        int pushesAfterEnd = 0;
                        for (int k = curr.Pos; k < j; k++)
                            pushesAfterEnd += result[k].PushCount;

                        int thenVal = prev.Ring.Count > 0 ? prev.Ring[^1] : -1;
                        int elseVal = curr.Ring.Count > 0 ? curr.Ring[^1] : -1;
                        if (thenVal < 0 || elseVal < 0) break;

                        int sharedNeeded = consumed.Length - 1 - pushesAfterEnd;
                        int sharedStart = Math.Max(0, prev.Ring.Count - sharedNeeded - 1);

                        var newConsumed = new int[consumed.Length];
                        int ci = 0;
                        for (; ci < sharedNeeded && sharedStart + ci < prev.Ring.Count; ci++)
                            newConsumed[ci] = prev.Ring[sharedStart + ci];
                        newConsumed[ci++] = elseVal;

                        int pushPos = curr.Pos;
                        for (int pi = 0; pi < pushesAfterEnd && ci < consumed.Length && pushPos < result.Count; pi++, ci++) {
                            while (pushPos < result.Count && pushPos < j) {
                                if (result[pushPos].PushCount > 0) break;
                                pushPos++;
                            }
                            if (pushPos < result.Count && pushPos < j)
                                newConsumed[ci] = pushPos++;
                        }

                        int phiIdx = sharedNeeded >= 0 && sharedNeeded < consumed.Length ? sharedNeeded : consumed.Length - 1;
                        var srcs = new int[consumed.Length];
                        var alts = new int[consumed.Length];
                        Array.Fill(srcs, -1);
                        Array.Fill(alts, -1);
                        srcs[phiIdx] = sourcePc;
                        alts[phiIdx] = thenVal;

                        result[j] = old with {
                            ConsumedFromPcs = newConsumed,
                            PhiSourcePcs = srcs,
                            PhiAltPcs = alts
                        };
                        break;
                    }
                }
            }
        }

        foreach (var (instIdx, labelId) in labelRefs) {
            if (labelPositions.TryGetValue(labelId, out int pos)) {
                var old = result[instIdx];
                result[instIdx] = old switch {
                    BranchIfFalse bif => new BranchIfFalse(pos) {
                        SourceNodeId = bif.SourceNodeId,
                        ConsumedFromPcs = bif.ConsumedFromPcs
                    },
                    Jump jmp => new Jump(pos) {
                        SourceNodeId = jmp.SourceNodeId,
                        ConsumedFromPcs = jmp.ConsumedFromPcs
                    },
                    _ => old
                };
            }
        }

        int raw = analysis.GetMetadata<MaxLocalsDepthMetadata>(root)?.Depth ?? 0;
        int maxDepth = Math.Max(1, raw + 1);
        return new LoweringResult(result, maxDepth, sourceRanges);
    }
}