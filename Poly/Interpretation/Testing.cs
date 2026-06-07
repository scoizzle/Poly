using System.Runtime.InteropServices;

namespace Poly.Interpretation;

class DataStack : IDisposable {
    private int _stackPointer;
    private readonly Memory<byte> _memory;
    private readonly IMemoryOwner<byte> _memoryOwner;

    public DataStack(int stackSize = 1024 * 1024) {
        _memoryOwner = MemoryPool<byte>.Shared.Rent(stackSize);
        _memory = _memoryOwner.Memory[..stackSize];
    }

    public void Dispose() {
        _memoryOwner.Dispose();
    }

    public void Push(Span<byte> data) {
        var resultingStackPointer = _stackPointer + data.Length;
        if (resultingStackPointer > _memory.Length)
            throw new InvalidOperationException("Stack overflow");
        var destination = _memory.Span[_stackPointer..resultingStackPointer];
        Debug.Assert(destination.Length == data.Length);
        data.CopyTo(destination);
        _stackPointer = resultingStackPointer;
    }

    public void Pop(Span<byte> destination) {
        if (_stackPointer < destination.Length)
            throw new InvalidOperationException("Stack underflow");

        var size = destination.Length;
        var resultingStackPointer = _stackPointer - size;
        Debug.Assert(resultingStackPointer >= 0);
        Debug.Assert((uint)resultingStackPointer < (uint)_stackPointer);
        Debug.Assert(destination.Length == size);

        var source = _memory.Span[resultingStackPointer.._stackPointer];
        Debug.Assert(source.Length == size);
        source.CopyTo(destination);
        _stackPointer = resultingStackPointer;
    }

    public void Push<T>(T value) where T : unmanaged {
        Span<T> data = MemoryMarshal.CreateSpan(ref value, 1);
        Span<byte> byteData = MemoryMarshal.AsBytes(data);
        Push(byteData);
    }

    public void Pop<T>(ref T value) where T : unmanaged {
        Span<T> data = MemoryMarshal.CreateSpan(ref value, 1);
        Span<byte> byteData = MemoryMarshal.AsBytes(data);
        Pop(byteData);
    }
}