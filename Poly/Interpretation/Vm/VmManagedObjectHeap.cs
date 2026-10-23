using System.Runtime.CompilerServices;

namespace Poly.Interpretation.Vm;

/// <summary>Object heap for the Poly VM. Reference-type values are stored
/// in a contiguous array indexed by handle. A handle is stable for the heap's
/// lifetime: only <see cref="Recycle"/> makes one reusable.</summary>
/// <remarks>Handle 0 is reserved as the ABI null/falsy sentinel and is never
/// allocated to a live object. The backing array is rented from
/// <see cref="ArrayPool{T}.Shared"/> and grows by doubling; <see cref="Dispose"/>
/// returns it cleared, so a short-lived execution allocates no heap buffer.
/// <para>A stored null is a value, never a free: <see cref="Set"/> and
/// <see cref="UnsafeSet"/> keep the handle live, so a store cannot hand a slot to a
/// later allocation that another holder still points at.</para></remarks>
public sealed class VmManagedObjectHeap : IDisposable {
    private object?[] _objects;
    private bool _disposed;
    /// <summary>Next free handle index. Starts at 1 so handle 0 remains the
    /// ABI null / falsy sentinel (never allocated to a live object).</summary>
    private int _count = 1;
    private HashSet<int>? _freeSlots;

    public VmManagedObjectHeap() : this(256) { }
    public VmManagedObjectHeap(int initialSlots) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialSlots, nameof(initialSlots));
        _objects = Rent(initialSlots);
    }

    /// <summary>Total number of allocated slots (including free-list recycled slots).</summary>
    public int Count => _count;

    /// <summary>Direct access to the backing object array. Use with caution —
    /// the array may contain null entries for free-list slots. Invalid after
    /// <see cref="Dispose"/>.</summary>
    public object?[] RawSlots => _objects;

    /// <summary>Allocates an object on the heap and returns its handle.
    /// Handles are recycled from the free-list when available.</summary>
    /// <param name="value">The object to store (may be null).</param>
    /// <returns>A non-zero handle that can be used to retrieve the object.</returns>
    public int Allocate(object? value) {
        if (value is null) return 0;
        if (_freeSlots is { Count: > 0 }) {
            int freeHandle = _freeSlots.First();
            _freeSlots.Remove(freeHandle);
            _objects[freeHandle] = value;
            return freeHandle;
        }
        int handle = _count;
        EnsureCapacity(handle);
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

    /// <summary>Sets the value at the given handle. A null value is stored as null:
    /// the handle stays live, so <see cref="Get"/> reads null and no later allocation
    /// can take the slot.</summary>
    /// <param name="handle">The heap handle to update.</param>
    /// <param name="value">The new object value (null stored as null).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the handle
    /// is 0 (reserved) or outside the valid range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the handle has already
    /// been recycled: a freed handle stays freed until <see cref="Allocate"/> reissues it.</exception>
    public void Set(int handle, object? value) {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Handle 0 is reserved for null");
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        RejectRecycled(handle);
        _objects[handle] = value;
    }

    /// <summary>Unsafe get — skips bounds checking. Only use when the handle
    /// is known to be valid (e.g. from safe code paths that already validated).</summary>
    /// <param name="handle">The heap handle.</param>
    /// <returns>The stored object (may be null).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? UnsafeGet(int handle) => _objects[handle];

    /// <summary>Unsafe set — skips bounds checking. A null value is stored as null:
    /// the handle stays live and the slot is not added to the free-list.</summary>
    /// <param name="handle">The heap handle.</param>
    /// <param name="value">The new object value.</param>
    /// <exception cref="InvalidOperationException">Thrown when the handle has already
    /// been recycled.</exception>
    public void UnsafeSet(int handle, object? value) {
        RejectRecycled(handle);
        _objects[handle] = value;
    }

    /// <summary>Releases a handle so the next <see cref="Allocate"/> may reuse its slot.
    /// The caller proves the object can never be observed again.</summary>
    /// <param name="handle">The handle to release (must be a live, previously issued handle).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the handle is 0 or was
    /// never issued.</exception>
    /// <exception cref="InvalidOperationException">Thrown on a double recycle: releasing one
    /// handle twice would put two allocations in one slot, the same silent alias as a
    /// use-after-free.</exception>
    public void Recycle(int handle) {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "Handle 0 is reserved for null");
        if ((uint)handle >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(handle), "Invalid heap handle");
        RejectRecycled(handle);
        _objects[handle] = null;
        (_freeSlots ??= []).Add(handle);
    }

    private void RejectRecycled(int handle) {
        if (_freeSlots?.Contains(handle) == true)
            throw new InvalidOperationException($"Heap handle {handle} is already free.");
    }

    /// <summary>Clears all objects and resets the free-list. Handle 0 remains
    /// reserved. After clear, all previously-issued handles are invalid.</summary>
    public void Clear() {
        Array.Clear(_objects, 0, _count);
        _count = 1; // keep handle 0 reserved
        _freeSlots?.Clear();
    }

    /// <summary>Returns the backing array to the shared pool, cleared so no
    /// heap object stays reachable. The heap must not be used afterwards.</summary>
    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        ArrayPool<object?>.Shared.Return(_objects, clearArray: true);
    }

    private void EnsureCapacity(int handle) {
        if (handle < _objects.Length)
            return;
        var grown = Rent(Math.Max(handle + 1, _objects.Length * 2));
        Array.Copy(_objects, grown, _count);
        var previous = _objects;
        _objects = grown;
        ArrayPool<object?>.Shared.Return(previous, clearArray: true);
    }

    private static object?[] Rent(int size) {
        var buffer = ArrayPool<object?>.Shared.Rent(size);
        Array.Clear(buffer);
        return buffer;
    }
}