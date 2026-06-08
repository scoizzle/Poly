using System.Collections.Generic;

namespace Poly.Interpretation;

public sealed class CallStack {
    private readonly Stack<StackFrame> _frames = new();

    public int Count => _frames.Count;

    public void Push(StackFrame frame) => _frames.Push(frame);
    public StackFrame Pop() => _frames.Pop();
    public StackFrame Peek() => _frames.Peek();

    public IReadOnlyList<StackFrame> Frames => _frames.ToArray();
}