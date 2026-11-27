using System.Runtime.CompilerServices;

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
/// A call that returns restores the frame state; a call that throws abandons the
/// run, and <see cref="Interpreter"/> restores it once at the entry point. A
/// catch-and-rethrow in this frame would make a deep unwind cost thousands of bytes
/// per live level, so the refusal thrown for a chain too deep could overflow the very
/// stack it protects (#1115).
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
    /// and stack pointer when the body returns; a body that throws abandons the run,
    /// and <see cref="Interpreter"/> restores the entry state once.</summary>
    public static long Invoke(
        VmState state, Action<VmState> function, ReadOnlySpan<long> args, int closureHandle = 0) {
        // A nested call recurses on the CLR stack, so an invoke chain's depth is bounded by what the calling
        // thread has left. A frame's slots cannot grow the backing array here: every live frame caches the
        // array it was entered with. The calling host sizes the stack for the chain up front (VmState's
        // value-stack slots), and the extent check below is the depth guard. The probe is a second, best-effort
        // check — its remaining-stack estimate is smaller than some compiled frames — so a recursion, or a
        // chain past the buffer, is the typed failure, never the process abort a stack overflow is (#1115).
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new InvalidOperationException("VM: value stack overflow.");
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
        // No catch here. A catch-and-rethrow at every live frame makes an unwind cost thousands of bytes per level,
        // so a depth refusal thrown 1,365 links deep aborted the stack it was protecting on linux/amd64 (#1115);
        // the entry point restores the frame state once instead.
        function(state);
        long result = slots[framePos];
        state.FramePos = savedFp;
        state.ClosureHandle = savedClosure;
        stack.SetStackPointer(callSp);
        return result;
    }
}