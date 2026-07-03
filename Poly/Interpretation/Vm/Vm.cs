namespace Poly.Interpretation.Vm;

// ── VM ABI (Calling Convention & Stack Layout) ──────────────────────────
//
// Call frame layout (one long slot of metadata):
// Before a Call* the N argument slots are on the stack:
//   [...stuff...][arg0][arg1]...[argN-1]
//                                      ^ SP
// The Call* handler pushes one metadata long:
//   Slot[sp++] = ((returnPC << 32) | (uint)(int)savedFrameBase)
//
// After call setup (0-relative to FB):
//   Slot[0]:               arg0            ← FB
//   Slot[1 .. ArgSlots-1]: arg1..argN-1
//   Slot[ArgSlots]:        metadata
//   Slot[ArgSlots+1]:      local0
//   Slot[ArgSlots+LocalCount]:  last local
//   Slot[ArgSlots+LocalCount+1]: first eval  ← SP
//
// Return convention (current — simplified since old pipeline deletion):
//   HandleCall and HandleCallClosure return the target function's entry PC.
//   EmitPrimitiveCall writes this directly to _pc, restores ring registers,
//   and jumps to EntryLabel. The dispatch switch then routes to the
//   function-body label at that PC. This avoids a state.ProgramCounter
//   round-trip.
//
//   EmitReturnOp writes the result to Slot[FB], sets SP = FB + 1, and jumps
//   to the compiled delegate's ExitLabel (ends program execution).
//   A proper frame-return primitive (restore caller PC/FB from metadata
//   via the packed metadata slot) will be added when cross-function calls
//   need to return to the caller.
//
// FrameBase sentinel: -1 = "no active frame" (top-level execution).

public static partial class Vm {
    public static InterpreterResult Execute(VmProgram program, params IEnumerable<object?> args) {
        var state = CreateState(program);
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[program.MaxActiveLocalsDepth];
        state.SetArgs(args);
        program.Delegate(state);

        if (state.Status == InterpreterStatus.Suspended)
            return InterpreterResult.Suspend();

        int sp = state.Stack.StackPointer;
        if (sp <= 0)
            return InterpreterResult.Void;

        long raw = state.Stack.RawSlots[sp - 1];
        int handle = (int)raw;

        // If the result is a heap handle, dereference to give callers
        // the actual CLR object rather than an opaque handle.
        // 0 and 1 are excluded as they're almost always boolean results.
        if (handle > 1 && handle < state.Heap.Count) {
            var heapObj = state.Heap.UnsafeGet(handle);
            return InterpreterResult.FromValue(heapObj);
        }

        return InterpreterResult.FromValue(raw);
    }

    public static InterpreterResult Execute(VmState state, params IEnumerable<object?> args) {
        var program = state.Program;
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[program.MaxActiveLocalsDepth];
        state.SetArgs(args);
        program.Delegate(state);

        if (state.Status == InterpreterStatus.Suspended)
            return InterpreterResult.Suspend();

        int sp = state.Stack.StackPointer;
        if (sp <= 0)
            return InterpreterResult.Void;

        long raw = state.Stack.RawSlots[sp - 1];
        int handle = (int)raw;

        // If the result is a heap handle, dereference to give callers
        // the actual CLR object rather than an opaque handle.
        // 0 and 1 are excluded as they're almost always boolean results.
        if (handle > 1 && handle < state.Heap.Count) {
            var heapObj = state.Heap.UnsafeGet(handle);
            return InterpreterResult.FromValue(heapObj);
        }

        return InterpreterResult.FromValue(raw);
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
    }

    internal static void HandleCallExternal(VmState state, int siteIndex) {
        var prog = state.Program;
        var callSites = prog.CallSites;
        if (callSites is null || (uint)siteIndex >= (uint)callSites.Count || callSites[siteIndex] is null)
            throw new InvalidOperationException($"CallExternal: no target at site {siteIndex}");
        callSites[siteIndex](state);
    }

    /// <summary>Public factory for VmState so expression trees can create instances.</summary>
    public static VmState CreateState(VmProgram program) => new(program);
}