namespace Poly.Interpretation.VirtualMachine;

/// <summary>µop-level synthesis pass using built-in heuristic functions,
/// mirroring the AST-level <c>HeuristicSynthesisPass</c>.  Each heuristic
/// checks the µop stream at a given position and may return a replacement
/// sequence.  Heuristics are self-contained static functions — no external
/// registration needed.</summary>
public sealed class UopHeuristicPass : IUopPass {
    public MicroOp[] Apply(MicroOp[] uops) {
        if (uops.Length < 2)
            return uops;

        var result = new List<MicroOp>(uops.Length);
        int i = 0;

        while (i < uops.Length) {
            bool matched = false;
            foreach (var heuristic in Heuristics) {
                if (heuristic(uops, i, out var replacement, out var consumed)) {
                    result.AddRange(replacement);
                    i += consumed;
                    matched = true;
                    break;
                }
            }
            if (!matched) {
                result.Add(uops[i]);
                i++;
            }
        }

        // Preserve reference identity when no change
        if (result.Count == uops.Length) {
            bool same = true;
            for (int j = 0; j < result.Count && same; j++)
                same = ReferenceEquals(result[j], uops[j]);
            if (same) return uops;
        }

        return [.. result];
    }

    // ── Heuristic delegate ──────────────────────────────────────────
    // Returns true if the pattern at uops[index] matches, with
    // replacement µops and the number of original µops consumed.

    private delegate bool UopHeuristic(MicroOp[] uops, int index,
        out MicroOp[] replacement, out int consumed);

    // ── Heuristic registry ──────────────────────────────────────────

    private static readonly List<UopHeuristic> Heuristics = [
        DataFlowSameLocalBinary,
        LoadLoadSameCommutativeBinary,
        UnaryThenCommutativeBinary,
    ];

    // ── Heuristic: same-variable CSE fusion ─────────────────────────
    // Pattern:  loadlocal v; loadlocal v; commutative_binary
    // Replace:  loadlocal v; dup; commutative_binary
    // Saves one loadlocal — previously handled in EmitBinary lowering.

    private static bool LoadLoadSameCommutativeBinary(MicroOp[] uops, int i,
        out MicroOp[] replacement, out int consumed) {
        replacement = null!;
        consumed = 0;

        if (i + 2 >= uops.Length) return false;
        if (uops[i] is not LoadLocalOp ll1) return false;
        if (uops[i + 1] is not LoadLocalOp ll2 || ll1.Index != ll2.Index) return false;
        if (!IsCommutativeBinary(uops[i + 2], out var immCheck) || immCheck is not null) return false;

        replacement = [
            new LoadLocalOp(ll1.Index, uops[i].Source),
            new DupOp(),
            uops[i + 2],    // binary
        ];
        consumed = 3;
        return true;
    }

    // ── Heuristic: unary-then-binary fusion ─────────────────────────
    // Pattern:  loadlocal v; unary; loadlocal v; commutative_binary
    // Replace:  loadlocal v; dup; unary; commutative_binary
    // Saves one redundant loadlocal per match.

    private static bool UnaryThenCommutativeBinary(MicroOp[] uops, int i,
        out MicroOp[] replacement, out int consumed) {
        replacement = null!;
        consumed = 0;

        if (i + 3 >= uops.Length) return false;

        if (uops[i] is not LoadLocalOp ll1) return false;
        if (!IsUnary(uops[i + 1])) return false;
        if (uops[i + 2] is not LoadLocalOp ll2 || ll1.Index != ll2.Index) return false;
        if (!IsCommutativeBinary(uops[i + 3], out var immCheck) || immCheck is not null) return false;

        replacement = [
            new LoadLocalOp(ll1.Index, uops[i].Source),
            new DupOp(),
            uops[i + 1],    // unary (preserve Source)
            uops[i + 3],    // binary (preserve Source)
        ];
        consumed = 4;
        return true;
    }

    // ── Heuristic: data-flow‑driven same-local binary fusion ─────────
    // Instead of matching literal µop adjacency, tracks stack depth to
    // find:  push(v) … push(v) … binop   where both pushes come from
    // the same local variable, regardless of intervening µops.
    //
    // Replace:  push(v); dup; intervening; binop
    // (dup at the first push, second push removed entirely).

    private static bool DataFlowSameLocalBinary(MicroOp[] uops, int i,
        out MicroOp[] replacement, out int consumed) {
        replacement = null!;
        consumed = 0;

        // Need at least: producer, (anything)*, producer, consumer
        if (i + 3 >= uops.Length) return false;
        if (!ReadsLocal(uops[i], out var firstLocal, out _)) return false;

        int stackDepth = 1; // after first µop

        // Scan forward tracking stack depth, looking for a second
        // read of the same local whose value survives to a binary op.
        for (int j = i + 1; j < uops.Length - 1; j++) {
            var (pop, push) = StackEffect(uops[j]);

            // Can't cross function calls — unknown stack effects
            if (uops[j] is CallOp or CallClosureOp or CallExternalOp) break;

            // Second read of the same local
            if (push > 0 && ReadsLocal(uops[j], out var secondLocal, out _)
                && secondLocal == firstLocal) {
                // Look for a binary op that consumes both values.
                // At this point, stackDepth = number of values between
                // the first push and the second push that are still alive.
                // The binary op we need consumes at depth+2 (first push),
                // depth+1 (second push = top), and pushes 1.
                int scanEnd = Math.Min(j + 8, uops.Length);
                int depthAfterSecond = stackDepth + push;

                for (int k = j + 1; k < scanEnd; k++) {
                    var (kp, kpush) = StackEffect(uops[k]);
                    if (kp > depthAfterSecond) break; // stack underflow relative to our values

                    // If this µop consumes exactly depthAfterSecond+1 values
                    // (the two pushes + anything between them), it's consuming both.
                    if (kp == depthAfterSecond + 1 && kpush == 1
                        && IsCommutativeBinary(uops[k], out var imm)
                        && imm is null) {
                        // Found! Replace from i to k:
                        //   loadlocal v; [intervening]; loadlocal v; [more]; binop
                        // → loadlocal v; dup; [intervening]; binop
                        int totalLen = k - i + 1;
                        var rep = new List<MicroOp>(totalLen);
                        rep.Add(new LoadLocalOp(firstLocal, uops[i].Source));
                        rep.Add(new DupOp());
                        // Copy intervening µops between first and second load
                        for (int m = i + 1; m < j; m++)
                            rep.Add(uops[m]);
                        // Copy µops between second load and binary op
                        for (int m = j + 1; m < k; m++)
                            rep.Add(uops[m]);
                        rep.Add(uops[k]); // the binary op

                        replacement = [.. rep];
                        consumed = totalLen;
                        return true;
                    }

                    depthAfterSecond += kpush - kp;
                }
                break; // only try the first matching second load
            }

            stackDepth += push - pop;
            if (stackDepth <= 0) break; // first value was consumed
        }

        return false;
    }

    // ── Stack effect & local read helpers ───────────────────────────

    /// <summary>(pops, pushes) for every µop type.
    /// Binary ops with <c>Immediate</c> set pop 1 (imm is second operand);
    /// with <c>Immediate == null</c> they pop 2 (both from stack).
    /// Unary ops pop 1, push 1.
    /// Store µops pop 1, push 0.
    /// Load µops pop 0, push 1.</summary>
    private static (int Pop, int Push) StackEffect(MicroOp op) => op switch {
        PushOp or LoadLocalOp or LoadArgOp
            or LoadUpvalueOp or LoadValueOp => (0, 1),
        NewArrayOp => (0, 1), // pushes dummy handle
        DupOp => (1, 2),
        PopOp or StoreLocalOp or StoreArgOp
            or StoreUpvalueOp or ThrowOp => (1, 0),
        ArrayStoreOp => (2, 0), // pop index, value
        NegOp or NotOp or BitNotOp or IncLocalOp => (1, 1),
        DivRemOp => (2, 2), // quotient + remainder
        ReturnOp or ReturnFromCallOp => (1, 0),
        CallOp or CallClosureOp or CallExternalOp => (0, 1),
        CommentOp => (0, 0),
        // Binary ops — check Immediate for stack effect
        AddOp a => a.Immediate is null ? (2, 1) : (1, 1),
        SubOp s => s.Immediate is null ? (2, 1) : (1, 1),
        MulOp m => m.Immediate is null ? (2, 1) : (1, 1),
        DivOp d => d.Immediate is null ? (2, 1) : (1, 1),
        EqOp e => e.Immediate is null ? (2, 1) : (1, 1),
        NeOp n => n.Immediate is null ? (2, 1) : (1, 1),
        LtOp l => l.Immediate is null ? (2, 1) : (1, 1),
        LeOp l => l.Immediate is null ? (2, 1) : (1, 1),
        GtOp g => g.Immediate is null ? (2, 1) : (1, 1),
        GeOp g => g.Immediate is null ? (2, 1) : (1, 1),
        ShlOp s => s.Immediate is null ? (2, 1) : (1, 1),
        ShrOp s => s.Immediate is null ? (2, 1) : (1, 1),
        BitAndOp b => b.Immediate is null ? (2, 1) : (1, 1),
        BitOrOp b => b.Immediate is null ? (2, 1) : (1, 1),
        BitXorOp b => b.Immediate is null ? (2, 1) : (1, 1),
        ArrayLoadOp => (2, 1), // pop handle, index
        StridedSetOp => (3, 0), // pop bits, start, stride, limit
        CountBitsOp => (1, 1), // pop bytecode handle
        // Default conservative: assume 0 pop, 1 push
        _ => (0, 1),
    };

    /// <summary>If the µop reads from a local variable, returns its index.</summary>
    private static bool ReadsLocal(MicroOp op, out int localIndex, out int writesToLocal) {
        switch (op) {
            case LoadLocalOp ll: localIndex = ll.Index; writesToLocal = -1; return true;
            case IncLocalOp il: localIndex = il.Index; writesToLocal = il.Index; return true;
            default: localIndex = -1; writesToLocal = -1; return false;
        }
    }

    private static bool IsUnary(MicroOp op) =>
        op is NegOp or NotOp or BitNotOp;

    private static bool IsCommutativeBinary(MicroOp op, out long? imm) {
        switch (op) {
            case AddOp a: imm = a.Immediate; return true;
            case MulOp m: imm = m.Immediate; return true;
            case BitAndOp b: imm = b.Immediate; return true;
            case BitOrOp b: imm = b.Immediate; return true;
            case BitXorOp b: imm = b.Immediate; return true;
            case EqOp e: imm = e.Immediate; return true;
            case NeOp n: imm = n.Immediate; return true;
            default: imm = null; return false;
        }
    }
}