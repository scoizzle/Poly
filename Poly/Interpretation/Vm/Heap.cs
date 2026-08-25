namespace Poly.Interpretation.Vm;

/// <summary>Object heap for the Poly VM. Reference-type values are stored
/// in a contiguous array indexed by handle. Handles are recycled via a
/// free-list when objects are set to null.</summary>
/// <remarks>Handle 0 is reserved as the ABI null/falsy sentinel and is never
/// allocated to a live object. The backing array grows by doubling.</remarks>
public sealed class Heap {
    private object?[] _objects = new object?[256];
    /// <summary>Next free handle index. Starts at 1 so handle 0 remains the
    /// ABI null / falsy sentinel (never allocated to a live object).</summary>
    private int _count = 1;
    private readonly HashSet<int> _freeSlots = [];

    /// <summary>Total number of allocated slots (including free-list recycled slots).</summary>
    public int Count => _count;

    /// <summary>Direct access to the backing object array. Use with caution —
    /// the array may contain null entries for free-list slots.</summary>
    public object?[] RawSlots => _objects;

    /// <summary>Allocates an object on the heap and returns its handle.
    /// Handles are recycled from the free-list when available.</summary>
    /// <param name="value">The object to store (may be null).</param>
    /// <returns>A non-zero handle that can be used to retrieve the object.</returns>
    public int Allocate(object? value) {
        if (value is null) return 0;
        if (_freeSlots.Count > 0) {
            int freeHandle = _freeSlots.First();
            _freeSlots.Remove(freeHandle);
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

    /// <summary>Retrieves the object at the given handle. Handle 0 always
    /// returns null (the ABI null sentinel).</summary>
    /// <param name="handle">The heap handle (must be &lt; <see cref="Count"/>).</param>
    /// <returns>The stored object, or null for handle 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the handle
    /// is outside the valid range.</exception>
    public object? Get(int handle) {
        if (handle == 0) return null;
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        return _objects[handle];
    }

    /// <summary>Sets the value at the given handle. If the new value is null,
    /// the handle is recycled via the free-list.</summary>
    /// <param name="handle">The heap handle to update.</param>
    /// <param name="value">The new object value (null to free the slot).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the handle
    /// is 0 (reserved) or outside the valid range.</exception>
    public void Set(int handle, object? value) {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Handle 0 is reserved for null");
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        Recycle(handle, value);
    }

    /// <summary>Unsafe get — skips bounds checking. Only use when the handle
    /// is known to be valid (e.g. from safe code paths that already validated).</summary>
    /// <param name="handle">The heap handle.</param>
    /// <returns>The stored object (may be null).</returns>
    public object? UnsafeGet(int handle) => _objects[handle];

    /// <summary>Unsafe set — skips bounds checking. If the value is null and
    /// the handle is not 0, the slot is added to the free-list.</summary>
    /// <param name="handle">The heap handle.</param>
    /// <param name="value">The new object value.</param>
    public void UnsafeSet(int handle, object? value) {
        Recycle(handle, value);
    }

    private void Recycle(int handle, object? value) {
        if (_freeSlots.Contains(handle))
            throw new InvalidOperationException($"Heap handle {handle} is already free.");
        bool wasLive = _objects[handle] is not null;
        _objects[handle] = value;
        if (value is null && wasLive)
            _freeSlots.Add(handle);
    }

    /// <summary>Clears all objects and resets the free-list. Handle 0 remains
    /// reserved. After clear, all previously-issued handles are invalid.</summary>
    public void Clear() {
        Array.Clear(_objects, 0, _count);
        _count = 1; // keep handle 0 reserved
        _freeSlots.Clear();
    }
}