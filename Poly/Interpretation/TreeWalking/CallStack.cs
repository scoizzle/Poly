using System.Collections.Generic;

using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Manages the call stack for the tree-walking virtual machine.
/// Simple wrapper around a stack of StackFrames for clarity and future extensibility.
/// </summary>
public sealed class CallStack {
    private readonly Stack<StackFrame> _frames = new();

    public void Push(StackFrame frame) => _frames.Push(frame);
    public StackFrame Pop() => _frames.Pop();
    public StackFrame Peek() => _frames.Peek();
    public bool IsEmpty => _frames.Count == 0;
    public int Count => _frames.Count;

    public IEnumerable<StackFrame> Frames => _frames;
}