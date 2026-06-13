namespace Poly.Interpretation.VirtualMachine;

internal sealed class Heap {
    private readonly List<object?> _objects = [];
    private readonly Stack<int> _freeSlots = [];

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
        return handle;
    }

    public object? Get(int handle) {
        if ((uint)handle >= (uint)_objects.Count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        return _objects[handle];
    }

    public void Set(int handle, object? value) {
        if ((uint)handle >= (uint)_objects.Count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        _objects[handle] = value;
        if (value is null)
            _freeSlots.Push(handle);
    }

    public object? UnsafeGet(int handle) => _objects[handle];

    public void UnsafeSet(int handle, object? value) {
        _objects[handle] = value;
        if (value is null)
            _freeSlots.Push(handle);
    }

    public void Clear() {
        _objects.Clear();
        _freeSlots.Clear();
    }
}