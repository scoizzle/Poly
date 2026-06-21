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

    public VmState(VmProgram program) {
        Program = program;
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
    }

    public void Dispose() => Stack.Dispose();
}