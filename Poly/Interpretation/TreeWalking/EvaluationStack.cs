using System;
using System.Buffers;

using Poly.Syntax;

namespace Poly.Interpretation.TreeWalking;

/// <summary>
/// High-performance operand stack using MemoryPool + Span.
/// This is the core execution stack for the tree-walking VM.
/// </summary>
public sealed class EvaluationStack : IDisposable {
    private readonly MemoryPool<object?> _pool;
    private IMemoryOwner<object?> _memoryOwner;
    private int _count = 0;
    private bool _disposed;

    public EvaluationStack(MemoryPool<object?>? pool = null, int initialCapacity = 64) {
        _pool = pool ?? MemoryPool<object?>.Shared;
        _memoryOwner = _pool.Rent(initialCapacity);
    }

    public void Push(object? value) {
        if (_count >= _memoryOwner.Memory.Length) {
            Grow();
        }
        _memoryOwner.Memory.Span[_count++] = value;
    }

    public object? Pop() {
        if (_count == 0) throw new InvalidOperationException("Evaluation stack underflow");
        return _memoryOwner.Memory.Span[--_count];
    }

    public object? Peek() {
        if (_count == 0) throw new InvalidOperationException("Evaluation stack underflow");
        return _memoryOwner.Memory.Span[_count - 1];
    }

    public int Count => _count;

    /// <summary>
    /// Returns a Span view of the current stack for efficient analysis by insight passes.
    /// </summary>
    public Span<object?> AsSpan() => _memoryOwner.Memory.Span.Slice(0, _count);

    public void Clear() => _count = 0;

    private void Grow() {
        int newCapacity = _memoryOwner.Memory.Length * 2;
        var newOwner = _pool.Rent(newCapacity);
        var newSpan = newOwner.Memory.Span;

        _memoryOwner.Memory.Span.Slice(0, _count).CopyTo(newSpan);
        _memoryOwner.Dispose();
        _memoryOwner = newOwner;
    }

    public void Dispose() {
        if (_disposed) return;
        _memoryOwner.Dispose();
        _disposed = true;
    }
}