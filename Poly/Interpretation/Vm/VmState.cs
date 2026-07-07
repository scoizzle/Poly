using Poly.Syntax;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Vm;

public sealed class VmState : IDisposable {
    public VmProgram Program { get; }
    public ValueStack Stack { get; } = new();
    public long[]? Registers { get; set; }
    public int ProgramCounter { get; set; }
    public int FrameBase { get; set; } = -1;
    public InterpreterStatus Status { get; set; } = InterpreterStatus.Running;
    public Heap Heap { get; } = new();
    public TextWriter? Trace { get; set; }

    /// <summary>Debug interrupt callback.  When set, the compiled delegate
    /// invokes this <em>before</em> each µop in Debug/Normal compilation mode,
    /// with <see cref="ProgramCounter"/> set to the current PC.  The callback
    /// can inspect state, set breakpoints externally, single-step, etc.</summary>
    public Action<VmState>? DebugInterrupt { get; set; }

    // Loop iteration limit safety (-1 = unlimited)
    public long MaxLoopIterations { get; set; } = -1;
    public long[]? LoopCounters { get; set; }

    // ── Closure/function call frame state ────────────────────────

    /// <summary>Closure handle active during a compiled function body.
    /// Set by the caller before invoking a function delegate; read by
    /// <c>LoadUpvalue</c>/<c>StoreUpvalue</c> to access captures.</summary>
    public int ClosureHandle { get; set; }

    /// <summary>Return PC saved by the caller at a call site.  Used by
    /// the invoked function to return to the correct µop (following the
    /// Call primitive).</summary>
    public int ReturnPC { get; set; }

    /// <summary>Caller's <see cref="FrameBase"/> saved before invoking a
    /// function.  Restored by the caller after the function returns.</summary>
    public int OldFrameBase { get; set; }

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
        FrameBase = -1;
        Status = InterpreterStatus.Running;
        Stack.Reset();
        Heap.Clear();
        LoopCounters = null;
        CurrentAstNode = null;
        CurrentNodeId = null;
    }

    public void Dispose() => Stack.Dispose();
}