using System;
using System.Collections.Generic;

using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Represents a point where execution was suspended. This enables the neurosymbolic
/// feedback loop where an LLM or authoring model can examine the live execution
/// state of lowered code and provide insights.
/// </summary>
public sealed record SuspendedExecution(
    InterpreterState State,
    string Reason,
    Node? AtNode,
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
public sealed class InterpreterState : IDisposable {
    private readonly MemoryPool<object?> _memoryPool;
    private bool _disposed;

    public EvaluationStack ValueStack { get; }
    public CallStack CallStack { get; } = new();

    public bool IsComplete { get; private set; }
    public InterpreterResult? LastResult { get; private set; }

    // Named variable storage (keyed by name, not node identity)
    public Dictionary<string, object?> Variables => CurrentFrame.Locals;
    // Suspension support - kept intentionally simple
    public bool IsSuspended { get; private set; }
    public string? SuspensionReason { get; private set; }
    public Node? SuspendedAtNode { get; private set; }

    public InterpreterState(MemoryPool<object?>? pool = null) {
        _memoryPool = pool ?? MemoryPool<object?>.Shared;
        ValueStack = new EvaluationStack(_memoryPool, 64);
    }

    /// <summary>
    /// Suspends execution at the current point. Returns a snapshot that can be
    /// introspected by insight analyzers or debugging tools.
    /// </summary>
    public SuspendedExecution Suspend(string reason, Node? atNode = null) {
        IsSuspended = true;
        SuspensionReason = reason;
        SuspendedAtNode = atNode ?? CallStack.Peek()?.CurrentNode;

        return new SuspendedExecution(
            this,
            reason,
            SuspendedAtNode,
            CallStack.Count,
            ValueStack.Count);
    }

    public void Resume() {
        IsSuspended = false;
        SuspensionReason = null;
        SuspendedAtNode = null;
    }

    public void Complete(InterpreterResult result) {
        IsComplete = true;
        LastResult = result;
    }

    public StackFrame CurrentFrame => CallStack.Peek();

    public void Dispose() {
        if (_disposed) return;
        ValueStack.Dispose();
        _disposed = true;
    }
}