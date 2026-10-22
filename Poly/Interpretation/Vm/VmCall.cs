namespace Poly.Interpretation.Vm;

/// <summary>
/// The VM's one call-frame protocol. A frame is a 2-word header (previous frame
/// pointer, saved stack pointer) followed by argument slots; the callee's result
/// is read from <c>slots[FramePointer]</c>, the first argument slot. Handle 0
/// remains the ABI null sentinel.
/// </summary>
/// <remarks>
/// Runtime calls (<see cref="Invoke"/>) and emitted function bodies agree on this
/// layout: <c>FramePos</c> is the first argument slot, a parameter is read at
/// <c>FramePos + index</c>, and the result is written to <c>slots[FramePos]</c>.
/// </remarks>
internal static class VmCall {
    /// <summary>Previous FP and saved SP precede the argument slots.</summary>
    public const int HeaderSize = 2;

    /// <summary>Argument slots in a frame. A parameterless callee still gets one
    /// slot so there is somewhere to write its result.</summary>
    public static int ArgumentSlots(int argumentCount) => Math.Max(1, argumentCount);

    /// <summary>The callee frame pointer: the first argument slot.</summary>
    public static int FramePointer(int callSp) => callSp + HeaderSize;

    /// <summary>Total stack slots a frame starting at <paramref name="callSp"/> needs.</summary>
    public static int Extent(int argumentCount) => HeaderSize + ArgumentSlots(argumentCount);

    /// <summary>Run <paramref name="function"/> in a new frame and return the result
    /// it wrote at <c>slots[FramePointer]</c>. Restores the frame pointer, closure,
    /// and stack pointer even when the body throws.</summary>
    public static long Invoke(
        VmState state, Action<VmState> function, ReadOnlySpan<long> args, int closureHandle = 0) {
        var stack = state.Stack;
        int callSp = stack.StackPointer;
        int need = callSp + Extent(args.Length);
        if (need > stack.RawSlots.Length)
            throw new InvalidOperationException("VM: value stack overflow.");
        stack.SetStackPointer(need);

        var slots = stack.RawSlots;
        slots[callSp] = state.FramePos;
        slots[callSp + 1] = callSp;
        for (int i = 0; i < args.Length; i++)
            slots[callSp + HeaderSize + i] = args[i];

        int savedFp = state.FramePos;
        int savedClosure = state.ClosureHandle;
        int framePos = FramePointer(callSp);
        state.FramePos = framePos;
        state.ClosureHandle = closureHandle;
        try {
            function(state);
            return slots[framePos];
        }
        finally {
            state.FramePos = savedFp;
            state.ClosureHandle = savedClosure;
            stack.SetStackPointer(callSp);
        }
    }
}