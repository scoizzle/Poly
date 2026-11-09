using System.Runtime.CompilerServices;

namespace Poly.Interpretation.Vm;

/// <summary>
/// Runs one step of a resumable body (an iterator or an async method) on its private frame:
/// copy the frame onto the shared stack above the stack pointer, run the compiled step, copy it
/// back. The shared stack pointer covers only a call's header and arguments, so a frame left on
/// the shared stack between two steps could be overwritten by a nested call.
/// </summary>
internal static class VmResumableFrame {
    public static void Run(VmState state, Action<VmState> step, long[] frame) {
        var stack = state.Stack;
        int callSp = stack.StackPointer;
        int framePos = VmCall.FramePointer(callSp);
        int need = framePos + frame.Length;
        if (need > stack.RawSlots.Length)
            throw new InvalidOperationException("VM: value stack overflow.");
        stack.SetStackPointer(need);

        var slots = stack.RawSlots;
        slots[callSp] = state.FramePos;
        slots[callSp + 1] = callSp;
        // Copy-in: the private frame onto the shared stack, above the stack pointer.
        Array.Copy(frame, 0, slots, framePos, frame.Length);

        int savedFp = state.FramePos;
        int savedClosure = state.ClosureHandle;
        state.FramePos = framePos;
        state.ClosureHandle = 0;
        try {
            step(state);
            // Copy-out: whatever the body spilled or advanced goes back to the private frame.
            Array.Copy(slots, framePos, frame, 0, frame.Length);
        }
        finally {
            state.FramePos = savedFp;
            state.ClosureHandle = savedClosure;
            stack.SetStackPointer(callSp);
        }
    }
}

/// <summary>
/// The private-frame layout of one compiled async body (#337 slice 2): how many words its frame
/// holds, and which three of them carry the body's answer to <see cref="VmAsyncMachine{T}"/>.
/// Registered against the compiled step delegate, because the call site holds only the callable slot.
/// </summary>
internal sealed record VmAsyncShape(int FrameSize, int StatusSlot, int ResultSlot, int AwaitedSlot) {
    private static readonly ConditionalWeakTable<Action<VmState>, VmAsyncShape> Shapes = new();

    public static void Register(Action<VmState> step, VmAsyncShape shape) => Shapes.AddOrUpdate(step, shape);

    public static VmAsyncShape Of(Action<VmState> step) =>
        Shapes.TryGetValue(step, out var shape)
            ? shape
            : throw new InvalidOperationException("VM: async body has no registered frame shape.");
}

/// <summary>The element of a <c>Task</c> (no result) produced by a resumable async body.</summary>
internal readonly struct VmAsyncVoid;

/// <summary>
/// The task an async method returns, and the machine that drives its body (#337 slice 2).
/// <see cref="Start"/> runs the body to its first <em>incomplete</em> await, or to its end, and
/// returns the method's task either way. At an incomplete await the body parks its resume point in
/// the private frame and the machine hooks the awaited task's continuation: when the awaited task
/// completes, the same compiled step runs again, enters its region by the dispatch, and the await
/// finishes by reading the now-complete task, so a fault is thrown at the await itself. A throw
/// out of any step faults the method's task instead of escaping to the caller.
/// </summary>
/// <remarks>
/// Resumption runs on the thread that completes the awaited task (no scheduler, no thread pool),
/// under a lock on the state, so two steps never run on one <see cref="VmState"/> at once. A
/// continuation that fires after the state was disposed faults the task: its stack is back in
/// the shared pool.
/// </remarks>
internal sealed class VmAsyncMachine<T> {
    private const long Suspended = 1L;
    private const long Completed = 2L;

    private readonly VmState _state;
    private readonly Action<VmState> _step;
    private readonly VmAsyncShape _shape;
    private readonly Func<VmManagedObjectHeap, long, T>? _decode;
    private readonly long[] _frame;
    private readonly TaskCompletionSource<T> _completion = new();

    public VmAsyncMachine(
        VmState state, Action<VmState>[] table, long[] arguments, Func<VmManagedObjectHeap, long, T>? decode) {
        _state = state;
        _step = table[0] ?? throw new InvalidOperationException("VM: async body is not compiled.");
        _shape = VmAsyncShape.Of(_step);
        _decode = decode;
        _frame = new long[Math.Max(_shape.FrameSize, arguments.Length)];
        Array.Copy(arguments, _frame, arguments.Length);
    }

    /// <summary>Runs the body until it suspends or finishes and returns the method's task.</summary>
    public Task<T> Start() {
        Step();
        return _completion.Task;
    }

    private void Step() {
        try {
            lock (_state) {
                if (_state.Stack.IsDisposed)
                    throw new ObjectDisposedException(nameof(VmAsyncMachine<T>),
                        "VM: the execution that created this async method has been disposed.");
                VmResumableFrame.Run(_state, _step, _frame);
                long status = _frame[_shape.StatusSlot];
                if (status == Completed) {
                    _completion.SetResult(
                        _decode is null ? default! : _decode(_state.Heap, _frame[_shape.ResultSlot]));
                    return;
                }
                if (status != Suspended)
                    throw new InvalidOperationException("VM: async body left without completing or awaiting.");
                var awaited = (Task)_state.Heap.UnsafeGet((int)_frame[_shape.AwaitedSlot])!;
                awaited.ContinueWith(
                    static (_, machine) => ((VmAsyncMachine<T>)machine!).Step(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (Exception ex) {
            _completion.TrySetException(ex);
        }
    }
}