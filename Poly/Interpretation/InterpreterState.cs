using System;
using System.Buffers;

using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation;

public sealed record SuspendedExecution(
    InterpreterState State,
    string Reason,
    Node AtNode,
    int CallStackDepth,
    int EvaluationStackDepth);

public sealed class InterpreterState(MemoryPool<object?>? pool = null) : IDisposable {
    public InterpreterStatus Status { get; private set; } = InterpreterStatus.Running;
    public EvaluationStack ValueStack { get; } = new(pool, 64);
    public CallStack CallStack { get; } = new();
    public AnalysisResult? AnalysisResult { get; internal set; }
    public NodeId? BreakpointSkipNodeId { get; internal set; }
    public InterpreterResult? LastResult { get; private set; }
    public string? SuspensionReason { get; private set; }
    public Node? SuspendedAtNode { get; private set; }

    public bool IsComplete => Status == InterpreterStatus.Completed;
    public bool IsSuspended => Status == InterpreterStatus.Suspended;
    public StackFrame CurrentFrame => CallStack.Peek();
    public Dictionary<string, object?> Variables => CurrentFrame.Variables;

    public SuspendedExecution Suspend(string reason, Node? atNode = null) {
        Status = InterpreterStatus.Suspended;
        SuspensionReason = reason;
        SuspendedAtNode = atNode ?? CurrentFrame.CurrentNode;
        return new SuspendedExecution(this, reason, SuspendedAtNode!, CallStack.Count, ValueStack.Count);
    }

    public void Resume() {
        Status = InterpreterStatus.Running;
        SuspensionReason = null;
        SuspendedAtNode = null;
    }

    public void Complete(InterpreterResult result) {
        Status = InterpreterStatus.Completed;
        LastResult = result;
    }

    public void Dispose() {
        ValueStack.Dispose();
    }
}