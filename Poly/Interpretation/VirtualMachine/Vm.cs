using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Poly.Interpretation.VirtualMachine;

// ── VM ABI (Calling Convention & Stack Layout) ──────────────────────────
//
// The VM uses a slot-based stack where each slot is a long (8 bytes).
// All arithmetic operates on long slots; type coercion to int/double/etc.
// happens only in ExtractResult (final value read-out).
//
// ── Call frame layout (one long slot of metadata) ──
// Before a Call* µop the N argument slots are on the stack:
//   [...stuff...][arg_0][arg_1]...[arg_N-1]
//                                      ^ SP
// The Call* handler pushes one metadata long (store):
//   Slot(sp++) = ((returnPC << 32) | (uint)(int)savedFrameBase)
//
// After call setup the layout is (0-relative to FB):
//   Slot[0]:               arg[0]            ← FB (frameBase)
//   Slot[1 .. ArgSlots-1]: arg[1..N-1]
//   Slot[ArgSlots]:        metadata
//   Slot[ArgSlots+1]:      local[0]          (if LocalCount > 0)
//   Slot[ArgSlots+LocalCount]:  last local
//   Slot[ArgSlots+LocalCount+1]: first eval  ← SP = FB + ArgSlots + LC + 1
//
// Return convention:
//   ReturnFromCallOp reads packed at Slot(FB + ArgSlots), writes result to
//   Slot(FB) (overwriting arg[0]), sets SP = FB + 1, restores FB/PC.
//
// ── FrameBase sentinel ──
// -1 means "no active frame" (top-level execution).  Call* saves the
// current FrameBase verbatim (including -1) into savedFrameBase.  Return
// restores it.  This is how top-level Return detects the end of
// execution vs. a nested function return.

internal static partial class Vm {
    public static InterpreterResult Execute(VmState state) {
        var prog = state.Program;
        if (prog is null) {
            state.Complete(InterpreterResult.Void);
            return InterpreterResult.Void;
        }

        state.Status = InterpreterStatus.Running;

        // Pre-load constants into heap
        for (int i = 0; i < prog.Constants.Count; i++)
            state.Heap.Allocate(prog.Constants[i]);

        // ── Compiled µop path (sole execution path) ──
        var loop = prog.EnsureCompiled();
        loop(state);

        // After the compiled delegate exits, extract the result.
        if (state.IsSuspended) {
            // Restore PC to the breakpoint site so resume re-executes from there
            state.PC = state.SavedPC;
            var suspendResult = InterpreterResult.Suspend();
            state.SetLastResultWithoutChangingStatus(suspendResult);
            return suspendResult;
        }

        int sp = state.Stack.SP;
        InterpreterResult final;
        if (sp <= 0) {
            final = InterpreterResult.Void;
        }
        else {
            long raw = state.Stack.RawSlots[sp - 1];
            var resultType = prog.ResultType;
            if (resultType is null || resultType == typeof(void))
                final = InterpreterResult.FromValue(raw);
            else if (resultType == typeof(int) || resultType == typeof(long)
                || resultType == typeof(uint) || resultType == typeof(ulong))
                final = InterpreterResult.FromValue(raw);
            else if (resultType == typeof(double) || resultType == typeof(float))
                final = InterpreterResult.FromValue(BitConverter.Int64BitsToDouble(raw));
            else if (resultType == typeof(bool))
                final = InterpreterResult.FromValue(raw != 0);
            else
                final = InterpreterResult.FromValue(raw);
        }
        if (!state.IsComplete && !state.IsSuspended)
            state.Complete(final);
        return final;
    }

    private static ExceptionRegion? FindRegion(
        IReadOnlyList<ExceptionRegion> regions, int pc) {
        for (int i = 0; i < regions.Count; i++) {
            var r = regions[i];
            if (pc >= r.TryStart && pc < r.TryEnd)
                return r;
        }
        return null;
    }

    // ── µop handler helpers (called via Expression.Call from compiled delegates) ──

    internal static void HandleCall(VmState state, int funcIndex, int argSlots) {
        var prog = state.Program!;
        var entry = prog.Functions[funcIndex];
        int newFp = state.Stack.SP - argSlots;
        int sp = state.Stack.SP;
        state.Stack.RawSlots[sp] = ((long)(state.PC + 1) << 32) | (uint)(int)state.FrameBase;
        state.Stack.SetSP(sp + 1);
        state.FrameBase = newFp;
        state.CachedArgSlots = argSlots;
        state.Stack.SetSP(newFp + argSlots + entry.LocalCount + 1);
        state.PC = entry.PC;
    }

    internal static void HandleCallClosure(VmState state) {
        var prog = state.Program!;
        int sp = state.Stack.SP;
        int closureHandle = (int)state.Stack.RawSlots[sp - state.CachedArgSlots];
        var closure = (Closure)state.Heap.Get(closureHandle)!;
        var entry = prog.Functions[closure.FuncIndex];
        int argSlots = entry.ArgSlots;
        int newFp = sp - argSlots;
        state.Stack.RawSlots[sp++] = ((long)(state.PC + 1) << 32) | (uint)(int)state.FrameBase;
        state.Stack.SetSP(sp);
        state.FrameBase = newFp;
        state.CachedArgSlots = argSlots;
        state.Stack.SetSP(newFp + argSlots + entry.LocalCount + 1);
        state.PC = entry.PC;
    }

    internal static void HandleCallExternal(VmState state, int siteIndex) {
        var prog = state.Program!;
        if ((uint)siteIndex >= (uint)prog.CallSites.Count || prog.CallSites[siteIndex] is null)
            throw new InvalidOperationException($"CallExternal: no target at site {siteIndex}");
        prog.CallSites[siteIndex](state);
    }

    internal static void HandleAllocClosure(VmState state, int funcIdx, int capCnt) {
        var c = new Closure(funcIdx, capCnt);
        var slots = state.Stack.RawSlots;
        int sp = state.Stack.SP;
        for (int i = capCnt - 1; i >= 0; i--)
            c.Captures[i] = slots[--sp];
        state.Stack.SetSP(sp);
        slots[sp] = state.Heap.Allocate(c);
        state.Stack.SetSP(sp + 1);
    }

    internal static long HandleLoadUpvalue(VmState state, int upi) {
        var slots = state.Stack.RawSlots;
        int handle = (int)slots[state.FrameBase];
        var closure = state.Heap.Get(handle) as Closure ?? throw new InvalidOperationException("LoadUpvalue: no closure at arg 0");
        return closure.Captures is not null && upi < closure.Captures.Length && closure.Captures[upi] is long lv ? lv : 0;
    }

    internal static void HandleStoreUpvalue(VmState state, int upi, long value) {
        var slots = state.Stack.RawSlots;
        int sp = state.Stack.SP;
        int handle = (int)slots[state.FrameBase];
        var closure = state.Heap.Get(handle) as Closure ?? throw new InvalidOperationException("StoreUpvalue: no closure at arg 0");
        if (closure.Captures is null || upi >= closure.Captures.Length)
            throw new InvalidOperationException($"StoreUpvalue: index {upi} out of range");
        closure.Captures[upi] = value;
    }

    internal static void HandleThrow(VmState state, long exValue) {
        int exVal = (int)exValue;
        var region = FindRegion(state.Program!.ExceptionRegions, state.PC);
        if (region is not null) {
            if (region.CatchStart >= 0) {
                int sp2 = state.Stack.SP;
                state.Stack.RawSlots[sp2] = exVal;
                state.Stack.SetSP(sp2 + 1);
                state.PendingExceptionValue = null;
                state.PC = region.CatchStart;
            }
            else if (region.FinallyStart is not null) {
                state.PendingExceptionValue = exVal;
                state.PC = region.FinallyStart.Value;
            }
        }
        else throw new InvalidOperationException("Unhandled VM exception: " + exVal);
    }

    internal static void HandleEndFinally(VmState state) {
        if (state.PendingExceptionValue is not null) {
            int exVal = state.PendingExceptionValue.Value;
            state.PendingExceptionValue = null;
            var region = FindRegion(state.Program!.ExceptionRegions, state.PC);
            if (region is not null && region.CatchStart >= 0) {
                int sp3 = state.Stack.SP;
                state.Stack.RawSlots[sp3] = exVal;
                state.Stack.SetSP(sp3 + 1);
                state.PC = region.CatchStart;
            }
            else throw new InvalidOperationException("Unhandled VM exception: " + exVal);
        }
    }

    /// <summary>Public factory for <c>VmState</c> so that AOT-compiled
    /// expression trees can create instances without accessing the
    /// internal constructor.</summary>
    public static VmState CreateState() => new VmState();

    /// <summary>Count set bits in a <c>long[]</c> word array using
    /// <c>TensorPrimitives.PopCount</c> for SIMD-accelerated per-element
    /// PopCount, then <c>TensorPrimitives.Sum</c> for vectorized
    /// reduction.  Processes the array in fixed-size chunks using
    /// <c>stackalloc</c> to avoid heap allocation.  Single call site,
    /// marked AggressiveInlining so the JIT inlines it into the
    /// compiled delegate.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CountBitsVectorized(long[] arr, int wordCount) {
        const int ChunkWords = 4096;
        long total = 0;
        int offset = 0;
        Span<ulong> counts = stackalloc ulong[ChunkWords];
        while (offset < wordCount) {
            int count = Math.Min(ChunkWords, wordCount - offset);
            var chunk = arr.AsSpan(offset, count);
            var ulongChunk = MemoryMarshal.Cast<long, ulong>(chunk);
            TensorPrimitives.PopCount(ulongChunk, counts[..count]);
            total += (long)TensorPrimitives.Sum<ulong>(counts[..count]);
            offset += count;
        }
        return total;
    }
}