using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Vm;

public record struct Word(long Value) {
    public static implicit operator Word(int value) => new(value);
    public static implicit operator int(Word word) => word.Value > IntMaxValue ? (int)IntMaxValue : (int)word.Value;

    public static implicit operator Word(long value) => new(value);
    public static implicit operator long(Word word) => word.Value;

    public bool IsHandle => Value < 0;
    const long IntMaxValue = int.MaxValue;
}

/// <summary>
/// Represents a single frame in the call stack of the virtual machine.
/// </summary>
/// <remarks>
/// |locals..arguments|previous frame pointer|locals..arguments|previous frame pointer|...
/// </remarks>
public record struct CallStackFrame(Word PreviousFramePointer, Word SavedStackPointer) {
    // Counts are known at the call site (compile time) and attached to the frame view.
    // They are *not* stored on the stack; only the two linkage values are pushed.
    public Word ArgumentCount { get; init; }
    public Word LocalCount { get; init; }

    public Word Size => ArgumentCount + LocalCount;
}

/// <summary>
/// Runtime supporting type for the frame-based ABI.
/// 
/// This is the concrete runtime implementation of the model shown in the
/// old static sim() inside CallStackFrame. It is what the *emitted* code
/// actually manipulates at execution time (via the long[] value stack).
///
/// Lowering (DirectVmAbiEmitter) uses a separate compile-time simulator
/// (see AbiCtx.EnterActivation / GetCompileTimeVariableOffset etc.)
/// to pre-compute offsets and decide exactly which "push two words +
/// advance SP" expressions to emit. That way we avoid almost all
/// runtime arithmetic inside a function body.
/// 
/// The runtime only needs to:
/// - Push the two linkage values (Previous + Saved) on call boundaries
/// - Maintain the linked frames so we can walk them for debug / suspend
/// - Provide a cheap Span<Word> over the current frame's locals for the
///   simplified DebugHook (Node + locals Span + Heap).
/// </summary>
public sealed class CallStack {
    private Word[] _storage;
    private int _sp;
    private readonly Stack<CallStackFrame> _activeFrames = new();

    public CallStack(int initialCapacity = 256) {
        _storage = new Word[initialCapacity];
        _sp = 0;
    }

    public int StackPointer { get => _sp; set => _sp = value; }

    public ref Word this[int index] => ref _storage[index];

    /// <summary>
    /// Returns a span over the locals for the given frame (for debug hook, etc.).
    /// </summary>
    public ReadOnlySpan<Word> GetLocals(in CallStackFrame frame) {
        int baseIndex = (int)frame.SavedStackPointer + 2; // after 2-word header
        return new ReadOnlySpan<Word>(_storage, baseIndex, (int)frame.LocalCount);
    }

    /// <summary>
    /// Returns a span over the arguments for the given frame.
    /// </summary>
    public ReadOnlySpan<Word> GetArguments(in CallStackFrame frame) {
        int headerStart = (int)frame.SavedStackPointer;
        int argStart = headerStart - (int)frame.ArgumentCount;
        return new ReadOnlySpan<Word>(_storage, argStart, (int)frame.ArgumentCount);
    }

    public ref Word GetLocal(in CallStackFrame frame, int index) {
        if ((uint)index >= (uint)frame.LocalCount)
            throw new IndexOutOfRangeException(nameof(index));
        int baseIndex = (int)frame.SavedStackPointer + 2;
        return ref _storage[baseIndex + index];
    }

    public ref Word GetArgument(in CallStackFrame frame, int index) {
        if ((uint)index >= (uint)frame.ArgumentCount)
            throw new IndexOutOfRangeException(nameof(index));
        int headerStart = (int)frame.SavedStackPointer;
        return ref _storage[headerStart - 1 - index];
    }

    /// <summary>
    /// Allocates a new frame on the stack by pushing the 2-word header
    /// and reserving space for locals (arguments must be pushed by caller first).
    /// </summary>
    public CallStackFrame AllocateFrame(Word previousFramePointer, Word savedStackPointer,
                                        int argumentCount, int localCount) {
        // Caller is expected to have already advanced SP for arguments.
        // We push exactly the two linkage values.
        Push(previousFramePointer);
        Push(savedStackPointer);

        int frameStartAfterHeader = _sp;
        _sp += localCount;

        var frame = new CallStackFrame(
            PreviousFramePointer: previousFramePointer,
            SavedStackPointer: savedStackPointer) {
            ArgumentCount = argumentCount,
            LocalCount = localCount
        };

        _activeFrames.Push(frame);
        return frame;
    }

    public void DeallocateFrame(CallStackFrame frame) {
        if (_sp != (int)frame.SavedStackPointer + 2 + (int)frame.LocalCount)
            throw new InvalidOperationException("Stack pointer does not match the end of the frame.");

        _sp = (int)frame.SavedStackPointer;
        _activeFrames.Pop();
    }

    public CallStackFrame CurrentFrame => _activeFrames.Count > 0 ? _activeFrames.Peek() : default;

    private void Push(Word value) {
        if (_sp >= _storage.Length)
            Array.Resize(ref _storage, _storage.Length * 2);
        _storage[_sp++] = value;
    }

    // For simulation / testing, similar to the old static sim()
    public static void RunSimulation() {
        var callStack = new CallStack(256);

        // === Top-level activation (like entering main) ===
        var mainFrame = callStack.AllocateFrame(
            previousFramePointer: 0,
            savedStackPointer: 0,
            argumentCount: 0,
            localCount: 1);

        callStack.GetLocal(mainFrame, 0) = 42;   // some local variable

        // === Simulating a call ===
        // 1. Caller evaluates arguments and places them in the argument area.
        //    In the real emitter this is done by emitting stores to the
        //    argument slots that will belong to the new frame.
        int argStart = callStack.StackPointer;
        callStack[argStart + 0] = 100;
        callStack[argStart + 1] = 200;
        callStack.StackPointer += 2;   // advance past args (caller does this)

        // 2. Allocate the callee frame.
        //    This pushes exactly the two linkage values and reserves space
        //    for the callee's locals. All layout knowledge (counts, offsets)
        //    comes from compile time.
        var calleeFrame = callStack.AllocateFrame(
            previousFramePointer: mainFrame.PreviousFramePointer,
            savedStackPointer: callStack.StackPointer,   // value *before* the 2-word header
            argumentCount: 2,
            localCount: 1);

        // Inside the callee we can read args and write locals using the frame view.
        // In the generated Expression tree these become direct indexed accesses
        // with compile-time offsets from the frame base that AllocateFrame established.
        long arg0 = callStack.GetArgument(calleeFrame, 0);
        long arg1 = callStack.GetArgument(calleeFrame, 1);
        callStack.GetLocal(calleeFrame, 0) = arg0 + arg1;

        // 3. Return from the call
        callStack.DeallocateFrame(calleeFrame);

        // Caller may clean arguments here depending on calling convention.
        // callStack.StackPointer = argStart;

        // === Return from top level ===
        callStack.DeallocateFrame(mainFrame);
    }
}

public sealed class VmState : IDisposable {
    public VmProgram Program { get; }
    public ValueStack Stack { get; } = new();
    public long[]? Registers { get; set; }
    public int ProgramCounter { get; set; }
    public InterpreterStatus Status { get; set; } = InterpreterStatus.Running;
    /// <summary>Persistent frame position. Set at suspend time; restored by the
    /// preamble before the PC-dispatch switch so resume starts at the right _fp.
    /// 0 = root frame (fresh execution).</summary>
    public int FramePos { get; set; }
    public Heap Heap { get; } = new();
    public TextWriter? Trace { get; set; }

    /// <summary>Debug interrupt callback.  When set, the compiled delegate
    /// invokes this <em>before</em> each µop in Debug/Normal compilation mode,
    /// with <see cref="ProgramCounter"/> set to the current PC.  The callback
    /// can inspect state, set breakpoints externally, single-step, etc.</summary>
    public Action<VmState>? DebugInterrupt { get; set; }

    /// <summary>
    /// Simplified debug hook invoked before each AST node in Debug/Normal compilation
    /// mode (when non-null). Receives the current AST <see cref="Node"/>, a
    /// <see cref="ReadOnlySpan{T}"/> over the current frame's local variables, and
    /// the <see cref="Heap"/> instance.
    ///
    /// This is the preferred hook for new code — it's simpler than
    /// <see cref="DebugInterrupt"/> (which passes the full <see cref="VmState"/>)
    /// and has zero emitted overhead when null.
    ///
    /// The locals <see cref="ReadOnlySpan{T}"/> is built at compile time from the
    /// frame model so no runtime iteration is needed.
    /// </summary>
    public Action<Node, ReadOnlySpan<long>, Heap>? DebugHook { get; set; }

    // Loop iteration limit safety (-1 = unlimited)
    public long MaxLoopIterations { get; set; } = -1;
    public long[]? LoopCounters { get; set; }

    // ── Closure/function call frame state ────────────────────────

    /// <summary>Closure handle active during a compiled function body.
    /// Set by the caller before invoking a function delegate; read by
    /// <c>LoadUpvalue</c>/<c>StoreUpvalue</c> to access captures.</summary>
    public int ClosureHandle { get; set; }

    /// <summary>Caller's frame position saved before invoking a
    /// function.  Restored by the caller after the function returns.</summary>
    public int OldFramePos { get; set; }

    /// <summary>
    /// In the direct (structured expression) execution path, this holds a reference
    /// to the AST node currently being executed or at which we are suspended.
    /// This allows debuggers, tracers, and suspend/resume logic to work directly
    /// with the symbolic AST rather than a synthetic PC.
    /// In the primitive path this may remain null (position derived from ProgramCounter + analysis).
    /// </summary>
    public Node? CurrentAstNode { get; set; }

    /// <summary>
    /// Lightweight, serializable identifier for the current AST node (preferred
    /// for suspended state that needs to be persisted or sent over the wire).
    /// </summary>
    public NodeId? CurrentNodeId { get; set; }

    public VmState(VmProgram program) {
        Program = program;
    }

    /// <summary>
    /// Seeds top-level arguments on the value stack.  Reference-type values
    /// are allocated on the heap and their handles placed in the parameter
    /// slots so <c>LoadSlot</c> / <c>CallExternalDirect</c> resolve correctly.
    /// <para>Call <em>before</em> <c>Interpreter.Execute</c>, after the program
    /// has been loaded (if any).  The number and order of arguments must match
    /// the compiled program's parameter layout.</para>
    /// </summary>
    public void SetArgs(params IEnumerable<object?> args) {
        var slots = Stack.RawSlots;
        foreach (var (i, arg) in args.Index()) {
            slots[i] = arg switch {
                null => 0L,
                long l => l,
                int iVal => iVal,
                bool b => b ? 1L : 0L,
                short s => s,
                byte bVal => bVal,
                _ => Heap.Allocate(arg)
            };
        }
    }

    public void Reset() {
        ProgramCounter = 0;
        Status = InterpreterStatus.Running;
        FramePos = 0;
        Stack.Reset();
        Heap.Clear();
        LoopCounters = null;
        CurrentAstNode = null;
        CurrentNodeId = null;
    }

    public void Dispose() => Stack.Dispose();
}