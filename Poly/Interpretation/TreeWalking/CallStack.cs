using System.Collections.Generic;

using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// Manages the call stack for the tree-walking virtual machine.
/// Protects against underflow (root frame may not be popped).
/// </summary>
public sealed class CallStack {
    private readonly Stack<StackFrame> _frames = new();

    public int Count => _frames.Count;
    public int Depth => _frames.Count;
    public bool IsEmpty => _frames.Count == 0;
    public StackFrame CurrentFrame => Peek();

    public void Push(StackFrame frame) {
        _frames.Push(frame);
    }

    public StackFrame Pop() {
        if (_frames.Count == 1) {
            throw new InvalidOperationException("Call stack underflow: no frames to pop.");
        }

        return _frames.Pop();
    }

    public StackFrame Peek() => _frames.Peek();

    public IEnumerable<StackFrame> Frames => _frames;
}