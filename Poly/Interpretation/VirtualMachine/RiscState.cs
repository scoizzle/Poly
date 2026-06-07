using System.Collections.Generic;

using Poly.Interpretation.TreeWalking;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.VirtualMachine;

/// <summary>
/// Execution state for the RISC stack VM.
/// Frames are represented purely as segments on the single operand stack (via frameBases).
/// On-stack frame headers contain retPC, savedPrevBase, callerPerspective (for creation-time absolute handle computation).
/// </summary>
internal sealed class RiscState : IDisposable {
    public RiscValueStack Stack { get; }
    public RiscHeap Heap { get; }
    public RiscProgram? Program { get; internal set; }
    public int PC { get; set; }
    public List<int> FrameBases { get; } = new();

    /// <summary>
    /// Append-only array of resolved call targets for CALL_EXTERNAL dispatch.
    /// Populated by lowering (each new lowering pass appends entries; old indices remain valid).
    /// Each entry is a <see cref="System.Reflection.MethodInfo"/> or <see cref="Delegate"/>.
    /// </summary>
    public List<object?> CallTargets { get; } = new();

    public AnalysisResult? AnalysisResult { get; internal set; }
    public NodeId? BreakpointSkipNodeId { get; internal set; }

    // Mirrors the tree-walker status for integration points later.
    public InterpreterStatus Status { get; internal set; } = InterpreterStatus.Running;
    public InterpreterResult? LastResult { get; private set; }

    public RiscState(MemoryPool<byte>? stackPool = null) {
        Stack = new RiscValueStack(stackPool);
        Heap = new RiscHeap();
    }

    public bool IsComplete => Status == InterpreterStatus.Completed;
    public bool IsSuspended => Status == InterpreterStatus.Suspended;

    public void Complete(InterpreterResult result) {
        Status = InterpreterStatus.Completed;
        LastResult = result;
    }

    internal void SetLastResultWithoutChangingStatus(InterpreterResult result) {
        LastResult = result;
    }

    public void Dispose() {
        Stack.Dispose();
    }
}