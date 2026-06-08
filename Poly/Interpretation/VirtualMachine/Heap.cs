namespace Poly.Interpretation.VirtualMachine;

internal sealed class Heap {
    private readonly List<object?> _objects = [];

    public Action<int, object?>? OnAllocate { get; set; }

    public int Count => _objects.Count;

    public int Allocate(object? value) {
        var handle = _objects.Count;
        _objects.Add(value);
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
    }

    internal IReadOnlyList<object?> DebugView => _objects;
}
