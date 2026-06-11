namespace Poly.Interpretation.VirtualMachine;

/// <summary>Hot-path execution state.  Keeps stack, frame, and PC as direct
/// fields — no class indirections.  Synced to <see cref="VmState"/> at
/// <c>CallExternal</c>/<c>CallClosure</c> boundaries.</summary>
internal ref struct VmExecutionContext {
    public Span<long> Stack;
    public int SP;
    public int FrameBase;
    public int CachedArgSlots;
    public int PC;
    public Bytecode Program;
    public bool DebugMode;

    public readonly int CodeLength => Program.CodeLength;
    public readonly int StackCapacity => Stack.Length;

    public void SyncToState(VmState state) {
        state.FrameBase = FrameBase;
        state.CachedArgSlots = CachedArgSlots;
        state.Stack.SetSP(SP);
        state.PC = PC;
    }

    public void SyncFromState(VmState state) {
        FrameBase = state.FrameBase;
        CachedArgSlots = state.CachedArgSlots;
        SP = state.Stack.SP;
        PC = state.PC;
    }
}