using System;
using System.Buffers;

namespace Poly.Interpretation;

public sealed class EvaluationStack : IDisposable {
    private readonly MemoryPool<object?> _pool;
    private IMemoryOwner<object?> _memoryOwner;
    private int _count;
    private bool _disposed;

    public EvaluationStack(MemoryPool<object?>? pool = null, int initialCapacity = 64) {
        _pool = pool ?? MemoryPool<object?>.Shared;
        _memoryOwner = _pool.Rent(initialCapacity);
    }

    public int Count => _count;

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

    public Span<object?> AsSpan() => _memoryOwner.Memory.Span.Slice(0, _count);

    public void Clear() => _count = 0;

    private void Grow() {
        var newOwner = _pool.Rent(_memoryOwner.Memory.Length * 2);
        _memoryOwner.Memory.Span[.._count].CopyTo(newOwner.Memory.Span);
        _memoryOwner.Dispose();
        _memoryOwner = newOwner;
    }

    public void Dispose() {
        if (!_disposed) {
            _memoryOwner.Dispose();
            _disposed = true;
        }
    }
}
