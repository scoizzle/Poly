namespace Poly.Interpretation.VirtualMachine;

public sealed class VmState : IDisposable {
    public ValueStack Stack { get; } = new();
    public Heap Heap { get; } = new();
    public Bytecode? Program { get; set; }
    public int PC { get; set; }
    public int FrameBase { get; set; } = -1;
    public int CachedArgSlots { get; set; }
    public int? PendingExceptionValue { get; set; }

    public InterpreterStatus Status { get; internal set; } = InterpreterStatus.Running;
    public InterpreterResult? LastResult { get; private set; }

    public bool IsComplete => Status == InterpreterStatus.Completed;
    public bool IsSuspended => Status == InterpreterStatus.Suspended;

    /// <summary>PCs where the VM should suspend before executing the µop.</summary>
    public HashSet<int>? BreakpointPCs { get; set; }

    /// <summary>PC at the point of suspension (set by the breakpoint check
    /// before the µop is executed).</summary>
    public int SavedPC { get; set; }

    public void Complete(InterpreterResult result) {
        Status = InterpreterStatus.Completed;
        LastResult = result;
    }

    internal void SetLastResultWithoutChangingStatus(InterpreterResult result) {
        LastResult = result;
    }

    /// <summary>When true, breakpoint checks are active (hot-path optimization
    /// skips them when false).</summary>
    public bool DebugMode { get; set; }

    public TextWriter? Trace { get; set; }

    public void Reset() {
        PC = 0;
        FrameBase = -1;
        CachedArgSlots = 0;
        PendingExceptionValue = null;
        Status = InterpreterStatus.Running;
        LastResult = null;
        Stack.Reset();
        Heap.Clear();
    }

    public void Dispose() => Stack.Dispose();
}