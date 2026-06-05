using System;
using System.Buffers;
using System.Collections.Generic;

using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.TreeWalking;

public enum InterpreterStatus {
    Running,
    Suspended,
    Completed
}

/// <summary>
/// Represents a point where execution was suspended.
/// </summary>
public sealed record SuspendedExecution(
    InterpreterState State,
    string Reason,
    Node AtNode,
    int CallStackDepth,
    int EvaluationStackDepth);

/// <summary>
/// Central execution state for the stack-based tree-walking virtual machine.
/// 
/// This class is designed to be:
/// - Suspendable at semantically meaningful points
/// - Fully introspectable (call stack, evaluation stack, current node)
/// - Agnostic to DomainModeling (only knows about Syntax.Node and AnalysisResult)
/// 
/// All domain-specific information comes through AnalysisResult metadata.
/// </summary>
public sealed class InterpreterState(MemoryPool<object?>? pool = null) {
    public InterpreterStatus Status { get; private set; } = InterpreterStatus.Running;
    public EvaluationStack ValueStack { get; } = new EvaluationStack(pool, 64);
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

    /// <summary>
    /// Suspends execution at the current point. Returns a snapshot that can be
    /// introspected by insight analyzers or debugging tools.
    /// </summary>
    public SuspendedExecution Suspend(string reason, Node? atNode = null) {
        Status = InterpreterStatus.Suspended;
        SuspensionReason = reason;
        SuspendedAtNode = atNode ?? CurrentFrame.CurrentNode;

        return new SuspendedExecution(
            this,
            reason,
            SuspendedAtNode,
            CallStack.Count,
            ValueStack.Count);
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