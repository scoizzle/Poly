namespace Poly.Interpretation.Vm;

public sealed class Heap {
    private object?[] _objects = new object?[256];
    /// <summary>Next free handle index. Starts at 1 so handle 0 remains the
    /// ABI null / falsy sentinel (never allocated to a live object).</summary>
    private int _count = 1;
    private readonly Stack<int> _freeSlots = [];

    public int Count => _count;
    public object?[] RawSlots => _objects;

    public int Allocate(object? value) {
        if (_freeSlots.TryPop(out int freeHandle)) {
            _objects[freeHandle] = value;
            return freeHandle;
        }
        int handle = _count;
        if (handle >= _objects.Length) {
            var newArr = new object?[_objects.Length * 2];
            Array.Copy(_objects, newArr, _count);
            _objects = newArr;
        }
        _objects[handle] = value;
        _count++;
        return handle;
    }

    public object? Get(int handle) {
        if (handle == 0) return null;
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        return _objects[handle];
    }

    public void Set(int handle, object? value) {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Handle 0 is reserved for null");
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        _objects[handle] = value;
        if (value is null)
            _freeSlots.Push(handle);
    }

    public object? UnsafeGet(int handle) => _objects[handle];

    public void UnsafeSet(int handle, object? value) {
        _objects[handle] = value;
        if (value is null && handle != 0)
            _freeSlots.Push(handle);
    }

    public void Clear() {
        Array.Clear(_objects, 0, _count);
        _count = 1; // keep handle 0 reserved
        _freeSlots.Clear();
    }
}