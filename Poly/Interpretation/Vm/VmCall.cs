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

    /// <summary>The frame protocol with its argument words named one by one, so a call
    /// site allocates nothing to make the call. One overload per arity the emitter passes
    /// directly; the words are stack-allocated into the span the core already takes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(VmState state, Action<VmState> function, long a0) {
        ReadOnlySpan<long> args = [a0];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(VmState state, Action<VmState> function, long a0, long a1) {
        ReadOnlySpan<long> args = [a0, a1];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(VmState state, Action<VmState> function, long a0, long a1, long a2) {
        ReadOnlySpan<long> args = [a0, a1, a2];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(VmState state, Action<VmState> function, long a0, long a1, long a2, long a3) {
        ReadOnlySpan<long> args = [a0, a1, a2, a3];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(
        VmState state, Action<VmState> function, long a0, long a1, long a2, long a3, long a4) {
        ReadOnlySpan<long> args = [a0, a1, a2, a3, a4];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(
        VmState state, Action<VmState> function, long a0, long a1, long a2, long a3, long a4, long a5) {
        ReadOnlySpan<long> args = [a0, a1, a2, a3, a4, a5];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(
        VmState state, Action<VmState> function,
        long a0, long a1, long a2, long a3, long a4, long a5, long a6) {
        ReadOnlySpan<long> args = [a0, a1, a2, a3, a4, a5, a6];
        return Invoke(state, function, args);
    }

    /// <inheritdoc cref="Invoke(VmState, Action{VmState}, long)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Invoke(
        VmState state, Action<VmState> function,
        long a0, long a1, long a2, long a3, long a4, long a5, long a6, long a7) {
        ReadOnlySpan<long> args = [a0, a1, a2, a3, a4, a5, a6, a7];
        return Invoke(state, function, args);
    }

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
        // A catch-and-rethrow rather than try/finally: the restore then runs inline on the
        // normal path, which is the only path a hot loop takes. A finally is emitted as a
        // funclet call even when nothing threw — a second prologue and epilogue per frame.
        try {
            function(state);
        }
        catch {
            state.FramePos = savedFp;
            state.ClosureHandle = savedClosure;
            stack.SetStackPointer(callSp);
            throw;
        }
        long result = slots[framePos];
        state.FramePos = savedFp;
        state.ClosureHandle = savedClosure;
        stack.SetStackPointer(callSp);
        return result;
    }
}