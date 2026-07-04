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
// Return convention:
//   EmitPrimitiveCall (inlined in the compiled delegate body) saves the
//   current FrameBase and return PC into a metadata slot, sets up the new
//   frame, and jumps directly to the function-body label.
//
//   EmitReturnOp writes the result to Slot[FB], sets SP = FB + 1, and jumps
//   to the compiled delegate's ExitLabel (ends program execution).
//
//   Frame-return (restore caller PC/FB from metadata) will be added when
//   cross-function calls need to return to the caller.
//
// FrameBase sentinel: -1 = "no active frame" (top-level execution).

public static partial class Vm {
    /// <summary>
    /// Execute <paramref name="program"/>, constructing a <see cref="VmState"/>
    /// internally and returning an <see cref="ExecutionResult"/> that owns the
    /// state.  The result carries both the <see cref="InterpreterResult"/> and
    /// the <see cref="VmState"/> for inspection or resumption.
    /// </summary>
    public static ExecutionResult Execute(VmProgram program, params IEnumerable<object?> args) =>
        Execute(program, s => s.SetArgs(args));

    /// <summary>
    /// Execute <paramref name="program"/> with state configuration before the
    /// compiled delegate runs.  The <paramref name="configure"/> callback can
    /// set state properties (e.g. <c>Trace</c>, <c>MaxLoopIterations</c>) and
    /// call <c>state.SetArgs(...)</c> to seed arguments.
    /// </summary>
    public static ExecutionResult Execute(VmProgram program, Action<VmState> configure) {
        var state = new VmState(program);
        configure(state);
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[state.Program.MaxActiveLocalsDepth];
        state.Program.Delegate(state);
        return new ExecutionResult(state, InterpretResult(state));
    }

    /// <summary>
    /// Execute or resume on an existing <see cref="VmState"/>.  This overload
    /// is <c>internal</c> because calling code should generally prefer the
    /// state-owning <see cref="ExecutionResult"/> API.
    /// </summary>
    internal static InterpreterResult Execute(VmState state, params IEnumerable<object?> args) {
        state.Status = InterpreterStatus.Running;
        state.Registers ??= new long[state.Program.MaxActiveLocalsDepth];
        state.SetArgs(args);
        state.Program.Delegate(state);
        return InterpretResult(state);
    }

    private static InterpreterResult InterpretResult(VmState state) {

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

}