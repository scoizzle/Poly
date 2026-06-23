namespace Poly.Interpretation.Vm;

public sealed class VmState : IDisposable {
    public VmProgram Program { get; }
    public ValueStack Stack { get; } = new();
    public long[]? Registers { get; set; }
    public int ProgramCounter { get; set; }
    public int FrameBase { get; set; } = -1;
    public int CachedArgSlots { get; set; }
    public InterpreterStatus Status { get; set; } = InterpreterStatus.Running;
    public int[]? Breakpoints { get; set; }
    public Heap Heap { get; } = new();
    public TextWriter? Trace { get; set; }

    // Profiling
    public long[]? InstructionCounters { get; set; }

    // Loop iteration limit safety (-1 = unlimited)
    public long MaxLoopIterations { get; set; } = -1;
    public long[]? LoopCounters { get; set; }

    // Breakpoint suspend/resume
    public bool NeedsRingRestore { get; set; }
    public int SavedRingDepth { get; set; }
    public bool SuspendResume { get; set; }
    public Action<VmState>? DebugInterrupt { get; set; }

    public VmState(VmProgram program) {
        Program = program;
    }

    /// <summary>
    /// Seeds top-level arguments on the value stack.  Reference-type values
    /// are allocated on the heap and their handles placed in the parameter
    /// slots so <c>LoadSlot</c> / <c>CallExternalDirect</c> resolve correctly.
    /// <para>Call <em>before</em> <c>Vm.Execute</c>, after constants have
    /// been loaded (if any).  The number and order of arguments must match
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
        CachedArgSlots = 0;
        Status = InterpreterStatus.Running;
        Stack.Reset();
        Heap.Clear();
        LoopCounters = null;
        NeedsRingRestore = false;
        SuspendResume = false;
    }

    public void Dispose() => Stack.Dispose();
}