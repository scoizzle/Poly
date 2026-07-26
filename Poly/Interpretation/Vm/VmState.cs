using Poly.Ast;
using Poly.Ast.Nodes;

namespace Poly.Interpretation.Vm;

/// <summary>Represents a single word of data in the VM's call stack.
/// A <see cref="Word"/> wraps a <see cref="long"/> value and provides
/// implicit conversions to/from <c>int</c> and <c>long</c>.</summary>
/// <remarks>Negative values are interpreted as heap handles
/// (<see cref="IsHandle"/>). This is the fundamental storage unit
/// in the frame-based ABI.</remarks>
public record struct Word(long Value) {
    /// <summary>Implicitly converts an <c>int</c> to a <c>Word</c>.</summary>
    public static implicit operator Word(int value) => new(value);

    /// <summary>Implicitly converts a <c>Word</c> to an <c>int</c>,
    /// clamping to <see cref="int.MaxValue"/> if the word's value
    /// exceeds the int range.</summary>
    public static implicit operator int(Word word) => word.Value > IntMaxValue ? (int)IntMaxValue : (int)word.Value;

    /// <summary>Implicitly converts a <c>long</c> to a <c>Word</c>.</summary>
    public static implicit operator Word(long value) => new(value);

    /// <summary>Implicitly converts a <c>Word</c> to a <c>long</c>.</summary>
    public static implicit operator long(Word word) => word.Value;

    /// <summary>True when the value is negative, indicating this word
    /// is a heap handle rather than a scalar value.</summary>
    public bool IsHandle => Value < 0;

    private const long IntMaxValue = int.MaxValue;
}

/// <summary>Represents a single frame in the call stack of the virtual machine.
/// The frame is linked via a two-word header (previous frame pointer and saved
/// stack pointer) stored on the stack itself.</summary>
/// <remarks>
/// Stack layout:
/// <code>
/// [argN-1..arg0] [previousFP] [savedSP] [local0..localM-1]
///                ^-- frame header (2 words)                    ^-- SP
/// </code>
/// Argument and local counts are known at compile time and attached to
/// this view — they are not stored on the stack. Only the two linkage
/// values are pushed at runtime.
/// </remarks>
/// <param name="PreviousFramePointer">The frame pointer of the caller.
/// -1 indicates the root (top-level) frame.</param>
/// <param name="SavedStackPointer">The stack pointer value just before
/// the two-word header was pushed, used to locate the argument area.</param>
public record struct CallStackFrame(Word PreviousFramePointer, Word SavedStackPointer) {
    /// <summary>Number of argument words in this frame.
    /// Set by the caller at compile time — not stored on the stack.</summary>
    public Word ArgumentCount { get; init; }

    /// <summary>Number of local variable words in this frame.
    /// Set by the caller at compile time — not stored on the stack.</summary>
    public Word LocalCount { get; init; }

    /// <summary>Total size of this frame in words (arguments + locals).</summary>
    public Word Size => ArgumentCount + LocalCount;
}

/// <summary>Runtime call stack for the frame-based VM ABI.
/// Provides the word storage, linked-frame tracking, and span views
/// over locals and arguments that the emitted code manipulates at
/// execution time.</summary>
/// <remarks>
/// The lowering pass (<see cref="DirectVmAbiEmitter"/>) uses a separate
/// compile-time simulator to pre-compute offsets, so this class only needs
/// to handle:
/// <list type="bullet">
///   <item>Pushing two linkage words (previousFP + savedSP) on call boundaries</item>
///   <item>Maintaining the linked-frame chain for debugging and suspension</item>
///   <item>Providing <see cref="Span{T}"/> views over frame locals</item>
/// </list>
/// </remarks>
public sealed class CallStack {
    private Word[] _storage;
    private int _sp;
    private readonly Stack<CallStackFrame> _activeFrames = new();

    /// <summary>Creates a new call stack with the given initial capacity.</summary>
    /// <param name="initialCapacity">Initial number of word slots (default 256).
    /// The backing array grows by doubling when full.</param>
    public CallStack(int initialCapacity = 256) {
        _storage = new Word[initialCapacity];
        _sp = 0;
    }

    /// <summary>Gets or sets the current stack pointer (number of words pushed).</summary>
    public int StackPointer { get => _sp; set => _sp = value; }

    /// <summary>Indexer into the raw word storage. Use frame-relative accessor
    /// methods (<see cref="GetLocal"/>, <see cref="GetArgument"/>) for
    /// safe access.</summary>
    /// <param name="index">The zero-based slot index.</param>
    public ref Word this[int index] => ref _storage[index];

    /// <summary>Returns a read-only span over the local variables of the specified
    /// frame. Safe for debug hooks, inspection, and serialization — does not
    /// allocate or copy.</summary>
    /// <param name="frame">The frame whose locals to view.</param>
    /// <returns>A span covering <c>frame.LocalCount</c> words starting just
    /// after the 2-word frame header.</returns>
    public ReadOnlySpan<Word> GetLocals(in CallStackFrame frame) {
        int baseIndex = (int)frame.SavedStackPointer + 2; // after 2-word header
        return new ReadOnlySpan<Word>(_storage, baseIndex, (int)frame.LocalCount);
    }

    /// <summary>Returns a read-only span over the arguments of the specified
    /// frame. Arguments are located before the 2-word frame header in reverse
    /// order (arg0 at the highest index).</summary>
    /// <param name="frame">The frame whose arguments to view.</param>
    /// <returns>A span covering <c>frame.ArgumentCount</c> words.</returns>
    public ReadOnlySpan<Word> GetArguments(in CallStackFrame frame) {
        int headerStart = (int)frame.SavedStackPointer;
        int argStart = headerStart - (int)frame.ArgumentCount;
        return new ReadOnlySpan<Word>(_storage, argStart, (int)frame.ArgumentCount);
    }

    /// <summary>Returns a reference to the local variable at the given index
    /// within the specified frame.</summary>
    /// <param name="frame">The frame whose local to access.</param>
    /// <param name="index">Zero-based index into the frame's locals.</param>
    /// <returns>A writable reference to the word at that position.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/>
    /// is not in <c>[0, frame.LocalCount)</c>.</exception>
    public ref Word GetLocal(in CallStackFrame frame, int index) {
        if ((uint)index >= (uint)frame.LocalCount)
            throw new IndexOutOfRangeException(nameof(index));
        int baseIndex = (int)frame.SavedStackPointer + 2;
        return ref _storage[baseIndex + index];
    }

    /// <summary>Returns a reference to the argument at the given index
    /// within the specified frame. Arguments are indexed from 0 (the first
    /// argument pushed).</summary>
    /// <param name="frame">The frame whose argument to access.</param>
    /// <param name="index">Zero-based index into the frame's arguments.</param>
    /// <returns>A writable reference to the word at that position.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/>
    /// is not in <c>[0, frame.ArgumentCount)</c>.</exception>
    public ref Word GetArgument(in CallStackFrame frame, int index) {
        if ((uint)index >= (uint)frame.ArgumentCount)
            throw new IndexOutOfRangeException(nameof(index));
        int headerStart = (int)frame.SavedStackPointer;
        return ref _storage[headerStart - 1 - index];
    }

    /// <summary>Allocates a new frame on the stack by pushing the 2-word header
    /// and reserving space for locals. Callers must have already pushed
    /// arguments onto the stack before calling this method.</summary>
    /// <param name="previousFramePointer">The previous frame's base pointer.
    /// Pass 0 for the root (top-level) frame.</param>
    /// <param name="savedStackPointer">The stack pointer position just before
    /// this frame's header is pushed — used to locate arguments.</param>
    /// <param name="argumentCount">Number of argument words already pushed.</param>
    /// <param name="localCount">Number of local variable words to reserve.</param>
    /// <returns>A <see cref="CallStackFrame"/> describing the allocated frame.</returns>
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

    /// <summary>Pops the specified frame from the stack, restoring the stack
    /// pointer to just before the frame's 2-word header. Validates that the
    /// current SP matches the expected end of the frame.</summary>
    /// <param name="frame">The frame to deallocate. Must be the most recently
    /// allocated (top) frame.</param>
    /// <exception cref="InvalidOperationException">Thrown when the current
    /// stack pointer does not match the expected end of the frame, indicating
    /// a stack imbalance.</exception>
    public void DeallocateFrame(CallStackFrame frame) {
        if (_sp != (int)frame.SavedStackPointer + 2 + (int)frame.LocalCount)
            throw new InvalidOperationException("Stack pointer does not match the end of the frame.");

        _sp = (int)frame.SavedStackPointer;
        _activeFrames.Pop();
    }

    /// <summary>Gets the most recently allocated (topmost) frame, or
    /// <c>default(CallStackFrame)</c> if the stack is empty.</summary>
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

/// <summary>Per-execution state for the Poly VM: value stack, heap,
/// registers, program counter, debug hooks, and loop iteration limits.
/// Created by <see cref="Interpreter.Execute"/> and exposed through
/// <see cref="ExecutionResult.State"/> for inspection and resumption.</summary>
/// <remarks>Owns a pooled <see cref="ValueStack"/> that must be disposed.
/// Use the <c>using</c> pattern: <c>using var result = Interpreter.Execute(program);</c></remarks>
public sealed class VmState : IDisposable {
    /// <summary>The compiled program being executed.</summary>
    public VmProgram Program { get; }

    /// <summary>The pooled value stack for scalars and heap handles.
    /// Backed by <see cref="ArrayPool{long}"/>.</summary>
    public ValueStack Stack { get; } = new();

    /// <summary>General-purpose register file used during µop execution.
    /// Allocated lazily — must be set before the program delegate runs.
    /// Default size is 256 slots.</summary>
    public long[]? Registers { get; set; }

    /// <summary>Current program counter (µop index) for debug/trace
    /// purposes. Updated by the compiled delegate in Debug/Normal mode.
    /// Set to 0 at the start of execution.</summary>
    public int ProgramCounter { get; set; }

    /// <summary>Current execution status. Set by the infrastructure and
    /// read by the delegate preamble to distinguish fresh execution from
    /// resumption after a suspend. Also updated when the delegate completes.</summary>
    public InterpreterStatus Status { get; set; } = InterpreterStatus.Running;

    /// <summary>Persistent frame position. Set at suspend time; restored by the
    /// preamble before the PC-dispatch switch so resume starts at the right _fp.
    /// 0 = root frame (fresh execution).</summary>
    public int FramePos { get; set; }

    /// <summary>The VM heap for reference-type values.
    /// Uses a free-list to recycle handles of freed objects.</summary>
    public Heap Heap { get; } = new();

    /// <summary>Optional trace writer for µop-level logging.
    /// When set, the compiled delegate emits trace lines before each µop.
    /// Null by default — zero overhead when null (single branch check).</summary>
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
    /// <summary>Simplified debug hook invoked before each AST node in
    /// Debug/Normal compilation mode (when non-null). Receives the current
    /// AST <see cref="Node"/>, a <see cref="ReadOnlySpan{T}"/> over the
    /// current frame's local variables, and the <see cref="Heap"/> instance.
    /// This is the preferred hook for new code — it's simpler than
    /// <see cref="DebugInterrupt"/> and has zero emitted overhead when null.</summary>
    public Action<Node, ReadOnlySpan<long>, Heap>? DebugHook { get; set; }

    /// <summary>Maximum number of loop iterations before the VM forcefully
    /// terminates execution as a safety guard. Set to -1 (default) for
    /// unlimited iterations. Useful for sandboxing untrusted code.
    /// Each loop header increments a counter; when the counter exceeds
    /// this limit, execution is suspended.</summary>
    public long MaxLoopIterations { get; set; } = -1;

    /// <summary>Per-loop iteration counters used to enforce
    /// <see cref="MaxLoopIterations"/>. Allocated lazily by the
    /// compiled delegate when loop limits are active.</summary>
    public long[]? LoopCounters { get; set; }

    // ── Closure/function call frame state ────────────────────────

    /// <summary>Closure handle active during a compiled function body.
    /// Set by the caller before invoking a function delegate; read by
    /// <c>LoadUpvalue</c>/<c>StoreUpvalue</c> to access captures.</summary>
    public int ClosureHandle { get; set; }

    /// <summary>Caller's frame position saved before invoking a
    /// function.  Restored by the caller after the function returns.</summary>
    public int OldFramePos { get; set; }

    /// <summary>Holds a reference to the AST node currently being executed
    /// or at which we are suspended. Allows debuggers, tracers, and
    /// suspend/resume logic to work directly with the symbolic AST rather
    /// than a synthetic PC. Set by the compiled delegate at node boundaries
    /// in Debug/Normal mode.</summary>
    public Node? CurrentAstNode { get; set; }

    /// <summary>Lightweight, serializable identifier for the current AST
    /// node. Preferred for suspended state that needs to be persisted or
    /// sent over the wire — unlike <see cref="CurrentAstNode"/>, this is
    /// a value type that survives serialization round-trips.</summary>
    public NodeId? CurrentNodeId { get; set; }

    /// <summary>Creates a new VM execution state for the given program.
    /// The state owns a pooled <see cref="ValueStack"/> and a
    /// <see cref="Heap"/> — dispose when done to return pool buffers.</summary>
    /// <param name="program">The compiled program to execute.</param>
    public VmState(VmProgram program) {
        Program = program;
    }

    /// <summary>Seeds top-level arguments on the value stack. Reference-type
    /// values are allocated on the heap and their handles placed in the
    /// parameter slots so that <c>LoadSlot</c>/<c>CallExternalDirect</c>
    /// resolve correctly.</summary>
    /// <remarks>Call <em>before</em> <c>Interpreter.Execute</c>, after the
    /// program has been loaded. The number and order of arguments must match
    /// the compiled program's parameter layout.</remarks>
    /// <param name="args">The argument values. Scalars (int, long, bool,
    /// short, byte) are stored directly; reference types are heap-allocated
    /// and their handles stored on the stack.</param>
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

    /// <summary>Resets the VM state to its initial condition, clearing the
    /// stack, heap, loop counters, and current node tracking. Does not
    /// change the <see cref="Program"/> reference. Useful for reusing a
    /// <see cref="VmState"/> across multiple executions of the same program.</summary>
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

    /// <summary>Releases the pooled value stack back to the
    /// <see cref="ArrayPool{long}"/>. After disposal,
    /// this state must not be used for execution.</summary>
    public void Dispose() => Stack.Dispose();
}