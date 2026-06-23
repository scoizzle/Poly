namespace Poly.Interpretation.Vm;

// ── VM ABI (Calling Convention & Stack Layout) ──────────────────────────
//
// Call frame layout (one long slot of metadata):
// Before a Call* the N argument slots are on the stack:
//   [...stuff...][arg_0][arg_1]...[arg_N-1]
//                                      ^ SP
// The Call* handler pushes one metadata long:
//   Slot(sp++) = ((returnPC << 32) | (uint)(int)savedFrameBase)
//
// After call setup (0-relative to FB):
//   Slot[0]:               arg[0]            ← FB
//   Slot[1 .. ArgSlots-1]: arg[1..N-1]
//   Slot[ArgSlots]:        metadata
//   Slot[ArgSlots+1]:      local[0]
//   Slot[ArgSlots+LocalCount]:  last local
//   Slot[ArgSlots+LocalCount+1]: first eval  ← SP
//
// Return convention:
//   ReturnFromCallOp reads packed at Slot(FB + ArgSlots), writes result to
//   Slot(FB), sets SP = FB + 1, restores FB/PC.
//
// FrameBase sentinel: -1 = "no active frame" (top-level execution).

public static partial class Vm {
    public static InterpreterResult Execute(VmState state) {
        var program = state.Program;
        state.Status = InterpreterStatus.Running;

        state.Registers ??= new long[program.MaxActiveLocalsDepth];

        program.Delegate(state);

        if (state.Status == InterpreterStatus.Suspended)
            return InterpreterResult.Suspend();

        int sp = state.Stack.StackPointer;
        if (sp <= 0)
            return InterpreterResult.Void;

        return InterpreterResult.FromValue(state.Stack.RawSlots[sp - 1]);
    }

    // ── µop handler helpers ──

    internal static void HandleCall(VmState state, int funcIndex, int argSlots) {
        var prog = state.Program;
        if ((uint)funcIndex >= (uint)prog.Functions.Count) {
            // No function — push a dummy 0 so the caller's
            // RawSlots[SP-1] access doesn't crash.
            state.Stack.SetStackPointer(1);
            state.Stack.RawSlots[0] = 0;
            return;
        }
        var entry = prog.Functions[funcIndex];
        int newFrameBase = state.Stack.StackPointer - argSlots;
        int sp = state.Stack.StackPointer;
        state.Stack.RawSlots[sp] = ((long)(state.ProgramCounter + 1) << 32)
            | (uint)state.FrameBase;
        state.Stack.SetStackPointer(sp + 1);
        state.FrameBase = newFrameBase;
        state.CachedArgSlots = argSlots;
        state.Stack.SetStackPointer(newFrameBase + argSlots + entry.LocalCount + 1);
        state.ProgramCounter = entry.PC;
    }

    internal static void HandleCallClosure(VmState state) {
        var prog = state.Program;
        int sp = state.Stack.StackPointer;
        int closureHandle = (int)state.Stack.RawSlots[sp - state.CachedArgSlots];
        var closure = (Closure)state.Heap.Get(closureHandle)!;
        var entry = prog.Functions[closure.FuncIndex];
        int argSlots = entry.ArgSlots;
        int newFrameBase = sp - argSlots;
        state.Stack.RawSlots[sp++] = ((long)(state.ProgramCounter + 1) << 32)
            | (uint)state.FrameBase;
        state.Stack.SetStackPointer(sp);
        state.FrameBase = newFrameBase;
        state.CachedArgSlots = argSlots;
        state.Stack.SetStackPointer(newFrameBase + argSlots + entry.LocalCount + 1);
        state.ProgramCounter = entry.PC;
    }

    internal static void HandleCallExternal(VmState state, int siteIndex) {
        var prog = state.Program;
        var callSites = prog.CallSites;
        if (callSites is null || (uint)siteIndex >= (uint)callSites.Count || callSites[siteIndex] is null)
            throw new InvalidOperationException($"CallExternal: no target at site {siteIndex}");
        callSites[siteIndex](state);
    }

    internal static void HandleAllocClosure(VmState state, int funcIdx, int capCnt) {
        var c = new Closure(funcIdx, capCnt);
        var slots = state.Stack.RawSlots;
        int sp = state.Stack.StackPointer;
        for (int i = capCnt - 1; i >= 0; i--)
            c.Captures[i] = slots[--sp];
        state.Stack.SetStackPointer(sp);
        slots[sp] = state.Heap.Allocate(c);
        state.Stack.SetStackPointer(sp + 1);
    }

    internal static long HandleLoadUpvalue(VmState state, int upi) {
        var slots = state.Stack.RawSlots;
        int handle = (int)slots[state.FrameBase];
        var closure = state.Heap.Get(handle) as Closure
            ?? throw new InvalidOperationException("LoadUpvalue: no closure at arg 0");
        return closure.Captures is not null && upi < closure.Captures.Length
            && closure.Captures[upi] is long lv ? lv : 0;
    }

    internal static void HandleStoreUpvalue(VmState state, int upi, long value) {
        var slots = state.Stack.RawSlots;
        int handle = (int)slots[state.FrameBase];
        var closure = state.Heap.Get(handle) as Closure
            ?? throw new InvalidOperationException("StoreUpvalue: no closure at arg 0");
        if (closure.Captures is null || upi >= closure.Captures.Length)
            throw new InvalidOperationException($"StoreUpvalue: index {upi} out of range");
        closure.Captures[upi] = value;
    }

    /// <summary>Public factory for VmState so expression trees can create instances.</summary>
    public static VmState CreateState(VmProgram program) => new(program);

    /// <summary>Checks whether <paramref name="pc"/> is in the breakpoints array.
    /// Used by <see cref="Instructions.BreakpointCheck"/> — factored here so
    /// the compiled delegate calls a simple CLR method instead of emitting
    /// complex expression tree logic.</summary>
    internal static bool HasBreakpoint(VmState state, int pc) {
        var bps = state.Breakpoints;
        return bps != null && Array.IndexOf(bps, pc) >= 0;
    }
}