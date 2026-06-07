using System.Collections.Generic;

namespace Poly.Interpretation.VirtualMachine;

internal sealed class RiscHeap {
    private readonly List<object?> _objects = new();

    public int Count => _objects.Count;

    /// <summary>
    /// Allocates a slot and returns the positive heap handle (index).
    /// </summary>
    public int Allocate(object? value) {
        var handle = _objects.Count;
        _objects.Add(value);
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
    }

    // For suspend/resume fidelity we may expose raw list later or snapshot.
    internal IReadOnlyList<object?> DebugView => _objects;
}