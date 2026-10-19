namespace Poly.Interpretation.Vm;

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

    /// <summary>Maximum number of loop iterations before the VM throws.
    /// Set to -1 (default) for unlimited iterations. Honored only in
    /// <see cref="CompilationMode.Normal"/>: each loop header increments
    /// <see cref="LoopTicks"/> and throws when the count exceeds this limit.
    /// <see cref="CompilationMode.NoDebug"/> does not emit the check.</summary>
    public long MaxLoopIterations { get; set; } = -1;

    /// <summary>Total loop-header visits this execution. Reset with the state.
    /// Compared against <see cref="MaxLoopIterations"/> when that limit is &gt;= 0.</summary>
    public long LoopTicks { get; set; }

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
    /// parameter slots so that <c>LoadSlot</c>
    /// resolve correctly.</summary>
    /// <remarks>Call <em>before</em> <c>Interpreter.Execute</c>, after the
    /// program has been loaded. The number and order of arguments must match
    /// the compiled program's parameter layout.</remarks>
    /// <param name="args">The argument values. Scalars (int, long, bool,
    /// short, byte) are stored directly; reference types are heap-allocated
    /// and their handles stored on the stack.</param>
    public void SetArgs(params IEnumerable<object?> args) {
        var slots = Stack.RawSlots;
        var types = Program.RootParameterClrTypes;
        foreach (var (i, arg) in args.Index()) {
            if (types is not null && i < types.Count && types[i] is Type pt
                && Nullable.GetUnderlyingType(pt) is not null)
                slots[i] = arg is null ? 0L : Heap.Allocate(arg);
            else
                slots[i] = ToRing(arg);
        }
    }

    internal long ToRing(object? arg) => RingCodec.ToRing(arg, Heap);

    /// <summary>Resets the VM state to its initial condition, clearing the
    /// stack, heap, loop ticks, and current node tracking. Does not
    /// change the <see cref="Program"/> reference. Useful for reusing a
    /// <see cref="VmState"/> across multiple executions of the same program.</summary>
    public void Reset() {
        ProgramCounter = 0;
        Status = InterpreterStatus.Running;
        FramePos = 0;
        Stack.Reset();
        Heap.Clear();
        LoopTicks = 0;
        CurrentAstNode = null;
        CurrentNodeId = null;
    }

    /// <summary>Releases the pooled value stack back to the
    /// <see cref="ArrayPool{long}"/>. After disposal,
    /// this state must not be used for execution.</summary>
    public void Dispose() => Stack.Dispose();
}