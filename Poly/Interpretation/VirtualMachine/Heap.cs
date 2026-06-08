using System.Collections.Generic;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class Heap {
    private readonly List<object?> _objects = [];
    private readonly Stack<int> _freeSlots = [];

    public Action<int, object?>? OnAllocate { get; set; }

    public int Count => _objects.Count;

    public int Allocate(object? value) {
        int handle;
        if (_freeSlots.TryPop(out int freeHandle)) {
            handle = freeHandle;
            _objects[handle] = value;
        }
        else {
            handle = _objects.Count;
            _objects.Add(value);
        }
        OnAllocate?.Invoke(handle, value);
        return handle;
    }

    public object? Get(int handle) {
        if (handle < 0 || handle >= _objects.Count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        return _objects[handle];
    }

    public void Set(int handle, object? value) {
        if (handle < 0 || handle >= _objects.Count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        _objects[handle] = value;
        if (value is null)
            _freeSlots.Push(handle);
    }

    internal IReadOnlyList<object?> DebugView => _objects;
}