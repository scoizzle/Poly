using System.Linq;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>Loop-invariant code motion: hoists pure µop subsequences that
/// appear identically in both a while-loop's condition and its body.
///
/// Detects patterns like the mandelbrot inner loop where
/// <c>zx*zx>>S</c> and <c>zy*zy>>S</c> are recomputed in both the
/// bailout check (condition) and the iteration update (body).</summary>
public sealed class LoopCsePass : IUopPass {
    private readonly BytecodeSpec? _spec;
    private readonly List<FunctionEntry>? _functions;

    public LoopCsePass(BytecodeSpec? spec, List<FunctionEntry>? functions = null) {
        _spec = spec;
        _functions = functions;
    }

    public MicroOp[] Apply(MicroOp[] uops) {
        if (_spec?.LoopBodies is null || _spec.LoopBodies.Count == 0)
            return uops;

        // ── Collect all candidate patches ──
        var patches = new List<Edit>();
        foreach (var loop in _spec.LoopBodies)
            CollectEdits(uops, loop, patches);

        if (patches.Count == 0)
            return uops;

        // ── Build new µop list with all edits applied ──
        var result = BuildAndRemap(uops, patches);

        // Update function LocalCount if temp locals were added
        if (_functions is not null && _functions.Count > 0) {
            int maxLocal = FindMaxLocal(result);
            // Find function 0 (the entry function) and update its LocalCount.
            // In practice, the only function with loops is the entry function.
            var f0 = _functions[0];
            if (maxLocal + 1 > f0.LocalCount)
                _functions[0] = f0 with { LocalCount = maxLocal + 1 };
        }

        return result;
    }

    // ── Edit description ─────────────────────────────────────────────
    // Each edit describes a replacement at a single original PC.
    // When Replace is > 0, that many µops are removed and Inserts are
    // added in their place.  Edits at the same PC are applied together.
    private sealed record Edit(int AtPC, int Replace, MicroOp[] Inserts);

    private static void CollectEdits(MicroOp[] uops, LoopBodyEntry loop, List<Edit> edits) {
        // Find JumpIfFalseOp that exits this loop
        int jmpIfPos = -1;
        for (int i = loop.ContPC; i < loop.BodyPC && i < uops.Length; i++) {
            if (uops[i] is JumpIfFalseOp jif && jif.Target == loop.EndPC) {
                jmpIfPos = i;
                break;
            }
        }
        if (jmpIfPos < 0) return;

        int condEnd = jmpIfPos;
        int bodyStart = jmpIfPos + 1;
        int bodyEnd = Math.Min(loop.BodyPC + loop.BodyLength, uops.Length);

        int condLen = condEnd - loop.ContPC;
        int bodyLen = bodyEnd - bodyStart;
        if (condLen < 2 || bodyLen < 2) return;

        // ── Skip loops whose condition has internal jumps ──
        // (short-circuit AND/OR would leave hoisted values on the stack
        //  when the short-circuit fires before the hoisted computation
        //  is evaluated).
        bool hasInternalJumps = false;
        for (int i = loop.ContPC; i < jmpIfPos; i++) {
            if (uops[i] is JumpIfFalseOp jif2 && jif2.Target != loop.EndPC) {
                hasInternalJumps = true;
                break;
            }
        }
        if (hasInternalJumps) return;

        // ── Find matching pure subsequences ──
        var candidates = new List<(int CondPC, int BodyPC, int Length)>();

        for (int ci = 0; ci < condLen; ci++) {
            for (int bi = 0; bi < bodyLen; bi++) {
                int len = 0;
                while (ci + len < condLen && bi + len < bodyLen
                    && OpsEqual(uops[loop.ContPC + ci + len], uops[bodyStart + bi + len]))
                    len++;

                if (len >= 3 && IsPureSequence(uops, loop.ContPC + ci, len)) {
                    int condPC = loop.ContPC + ci;
                    int bodyPC = bodyStart + bi;
                    if (IsSafeToHoist(uops, condPC, bodyPC, len))
                        candidates.Add((condPC, bodyPC, len));
                }
            }
        }

        if (candidates.Count == 0) return;

        // ── Greedy selection (longest first, non-overlapping) ──
        candidates.Sort((a, b) => b.Length.CompareTo(a.Length));
        var used = new HashSet<int>();
        int nextTemp = FindMaxLocal(uops) + 1;

        foreach (var (condPC, bodyPC, len) in candidates) {
            if (Overlaps(used, condPC, len) || Overlaps(used, bodyPC, len))
                continue;
            for (int i = 0; i < len; i++) { used.Add(condPC + i); used.Add(bodyPC + i); }

            int temp = nextTemp++;

            // Hoisted sequence: copy the original µops + storelocal
            var hoisted = new MicroOp[len + 1];
            Array.Copy(uops, condPC, hoisted, 0, len);
            hoisted[len] = new StoreLocalOp(temp);

            // Condition replacement: loadlocal
            // Body replacement: loadlocal
            edits.Add(new Edit(condPC, len, [new LoadLocalOp(temp)]));
            edits.Add(new Edit(bodyPC, len, [new LoadLocalOp(temp)]));
            // Insert hoisted seq + storelocal before loop condition
            edits.Add(new Edit(loop.ContPC, 0, hoisted));
        }
    }

    private static bool Overlaps(HashSet<int> set, int start, int len) {
        for (int i = start; i < start + len; i++)
            if (set.Contains(i)) return true;
        return false;
    }

    // ── Array builder with PC remapping ───────────────────────────────

    private static MicroOp[] BuildAndRemap(MicroOp[] uops, List<Edit> edits) {
        // Group edits by AtPC, keeping them in insertion order per PC
        var groups = new Dictionary<int, List<Edit>>();
        foreach (var e in edits) {
            if (!groups.ContainsKey(e.AtPC))
                groups[e.AtPC] = [];
            groups[e.AtPC].Add(e);
        }

        // Track which original PCs had pure-insert edits (replace==0)
        // at those positions.  Back edges targeting these PCs need to
        // jump to the start of the inserts, not to the µop that was moved
        // after the inserts.
        var insertTargets = new Dictionary<int, int>(); // oldPC → insertStart (newPC)

        // ── Build the new µop array and old→new PC map ──
        var result = new List<MicroOp>(uops.Length + edits.Sum(e => e.Inserts.Length - e.Replace));
        int[] oldToNew = new int[uops.Length];
        Array.Fill(oldToNew, -1);

        for (int oldPC = 0; oldPC < uops.Length;) {
            if (groups.TryGetValue(oldPC, out var grp)) {
                int replace = 0;
                var inserts = new List<MicroOp>();
                foreach (var e in grp) {
                    replace += e.Replace;
                    inserts.AddRange(e.Inserts);
                }

                if (replace == 0) {
                    // Pure insert: the original µop at oldPC moves after inserts.
                    // Maps oldPC → position of moved µop.
                    // Also records the insert start for back-edge correction.
                    int insertStart = result.Count;
                    insertTargets[oldPC] = insertStart;
                    oldToNew[oldPC] = result.Count + inserts.Count;
                    result.AddRange(inserts);
                    result.Add(uops[oldPC]);
                    oldPC++;
                }
                else {
                    for (int j = 0; j < replace && oldPC + j < uops.Length; j++)
                        oldToNew[oldPC + j] = result.Count;

                    result.AddRange(inserts);
                    oldPC += replace;
                }
            }
            else {
                oldToNew[oldPC] = result.Count;
                result.Add(uops[oldPC]);
                oldPC++;
            }
        }

        // ── Remap jump targets via old→new mapping ──
        for (int i = 0; i < result.Count; i++) {
            var op = result[i];
            if (op is JumpOp jmp) {
                int newTarget = Remap(jmp.Target, oldToNew, insertTargets);
                if (newTarget != jmp.Target)
                    result[i] = new JumpOp(newTarget, jmp.Source);
            }
            else if (op is JumpIfFalseOp jif) {
                int newTarget = Remap(jif.Target, oldToNew, insertTargets);
                if (newTarget != jif.Target)
                    result[i] = new JumpIfFalseOp(newTarget, jif.Source);
            }
        }

        return [.. result];
    }

    private static int Remap(int oldPC, int[] oldToNew, Dictionary<int, int> insertTargets) {
        if (oldPC < 0 || oldPC >= oldToNew.Length)
            return oldPC;

        // If a pure-insert happened at this PC, the loop header starts
        // at the insert position (before the original µop).  Back edges
        // targeting ContPC should go to the start of the hoisted computation.
        if (insertTargets.TryGetValue(oldPC, out int insertStart))
            return insertStart;

        int npc = oldToNew[oldPC];
        if (npc >= 0) return npc;

        for (int fwd = oldPC + 1; fwd < oldToNew.Length; fwd++)
            if (oldToNew[fwd] >= 0) return oldToNew[fwd];
        for (int bwd = oldPC - 1; bwd >= 0; bwd--)
            if (oldToNew[bwd] >= 0) return oldToNew[bwd];

        return oldPC;
    }

    // ── µop comparison ──────────────────────────────────────────────

    private static bool OpsEqual(MicroOp a, MicroOp b) {
        if (a.GetType() != b.GetType()) return false;
        return a switch {
            LoadLocalOp lla => b is LoadLocalOp llb && lla.Index == llb.Index,
            PushOp pa => b is PushOp pb && pa.Value == pb.Value,
            AddOp aa => b is AddOp ab && aa.Immediate == ab.Immediate,
            SubOp sa => b is SubOp sb && sa.Immediate == sb.Immediate,
            MulOp ma => b is MulOp mb && ma.Immediate == mb.Immediate,
            DivOp da => b is DivOp db && da.Immediate == db.Immediate,
            EqOp ea => b is EqOp eb && ea.Immediate == eb.Immediate,
            NeOp na => b is NeOp nb && na.Immediate == nb.Immediate,
            LtOp la => b is LtOp lb && la.Immediate == lb.Immediate,
            LeOp lea => b is LeOp leb && lea.Immediate == leb.Immediate,
            GtOp ga => b is GtOp gb && ga.Immediate == gb.Immediate,
            GeOp gea => b is GeOp geb && gea.Immediate == geb.Immediate,
            ShlOp sha => b is ShlOp shb && sha.Immediate == shb.Immediate,
            ShrOp sra => b is ShrOp srb && sra.Immediate == srb.Immediate,
            BitAndOp baa => b is BitAndOp bab && baa.Immediate == bab.Immediate,
            BitOrOp boa => b is BitOrOp bob && boa.Immediate == bob.Immediate,
            BitXorOp bxa => b is BitXorOp bxb && bxa.Immediate == bxb.Immediate,
            NegOp or NotOp or BitNotOp or DupOp or CommentOp => true,
            _ => false
        };
    }

    // ── Purity ─────────────────────────────────────────────────────

    private static readonly HashSet<Type> PureTypes = [
        typeof(LoadLocalOp), typeof(LoadArgOp),
        typeof(LoadUpvalueOp), typeof(LoadValueOp),
        typeof(PushOp), typeof(DupOp),
        typeof(NegOp), typeof(NotOp), typeof(BitNotOp),
        typeof(AddOp), typeof(SubOp), typeof(MulOp), typeof(DivOp),
        typeof(EqOp), typeof(NeOp), typeof(LtOp), typeof(LeOp),
        typeof(GtOp), typeof(GeOp),
        typeof(ShlOp), typeof(ShrOp),
        typeof(BitAndOp), typeof(BitOrOp), typeof(BitXorOp),
        typeof(CommentOp),
    ];

    private static bool IsPure(MicroOp op) => PureTypes.Contains(op.GetType());

    private static bool IsPureSequence(MicroOp[] ops, int start, int len) {
        for (int i = start; i < start + len; i++)
            if (!IsPure(ops[i]))
                return false;
        return true;
    }

    private static HashSet<int> CollectReadLocals(MicroOp[] ops, int start, int len) {
        var locals = new HashSet<int>();
        for (int i = start; i < start + len; i++) {
            if (ops[i] is LoadLocalOp ll)
                locals.Add(ll.Index);
        }
        return locals;
    }

    private static bool IsSafeToHoist(MicroOp[] uops, int condPC, int bodyPC, int len) {
        var reads = CollectReadLocals(uops, condPC, len);
        if (reads.Count == 0) return true;
        int scanStart = condPC + len;
        int scanEnd = bodyPC;
        if (scanStart >= scanEnd) return false;
        for (int i = scanStart; i < scanEnd && i < uops.Length; i++) {
            if (uops[i] is StoreLocalOp sl && reads.Contains(sl.Index)) return false;
            if (uops[i] is IncLocalOp il && reads.Contains(il.Index)) return false;
        }
        return true;
    }

    private static int FindMaxLocal(MicroOp[] uops) {
        int max = -1;
        foreach (var op in uops) {
            if (op is LoadLocalOp ll && ll.Index > max) max = ll.Index;
            else if (op is StoreLocalOp sl && sl.Index > max) max = sl.Index;
            else if (op is IncLocalOp il && il.Index > max) max = il.Index;
        }
        return max;
    }
}