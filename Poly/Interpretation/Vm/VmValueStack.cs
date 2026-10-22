namespace Poly.Interpretation.Vm;

/// <summary>Pooled value stack for the Poly VM. Stores 64-bit scalar values
/// and heap handles using <see cref="ArrayPool{long}.Shared"/> to minimize
/// allocation overhead during execution.</summary>
/// <remarks>
/// All public members are inlined aggressively by the JIT. The stack grows
/// by doubling when full and returns its buffer to the pool on <see cref="Dispose"/>.
/// </remarks>
public sealed class VmValueStack : IDisposable {
    private long[] _slots;
    private bool _disposed;

    public VmValueStack() : this(256) { }
    public VmValueStack(int initialSlots) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialSlots, nameof(initialSlots));
        _slots = Rent(initialSlots);
    }

    /// <summary>Current stack height (number of elements).</summary>
    public int StackPointer { get; private set; }

    /// <summary>Pushes a 64-bit value onto the stack. Grows the backing array
    /// automatically if full. Inlined in the common (no-grow) path.</summary>
    /// <param name="value">The value to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(long value) {
        if (StackPointer < _slots.Length) { _slots[StackPointer++] = value; return; }
        GrowNoInline(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowNoInline(long value) {
        EnsureCapacity(StackPointer + 1);
        _slots[StackPointer++] = value;
    }

    /// <summary>Pushes a 32-bit integer (widened to 64-bit).</summary>
    /// <param name="value">The value to push.</param>
    public void Push(int value) => Push((long)value);

    /// <summary>Pops and returns the top value. Throws on underflow.</summary>
    /// <returns>The popped value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stack is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Pop() {
        if (StackPointer > 0) return _slots[--StackPointer];
        return ThrowUnderflow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ThrowUnderflow() =>
        throw new InvalidOperationException("Stack underflow");

    /// <summary>Drops the top <paramref name="count"/> values without reading them.
    /// Throws on underflow.</summary>
    /// <param name="count">Number of values to drop.</param>
    /// <exception cref="InvalidOperationException">Thrown when dropping more
    /// values than are on the stack.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Drop(int count) {
        var sp = StackPointer - count;
        if ((uint)sp > (uint)StackPointer)
            ThrowUnderflow();
        StackPointer = sp;
    }

    /// <summary>Direct access to the backing array for bulk operations.
    /// Use with caution — the array may be larger than <see cref="StackPointer"/>.</summary>
    public long[] RawSlots => _slots;

    /// <summary>Sets the stack pointer to a specific value, growing the backing
    /// array when the value exceeds its length. Intended for frame management in
    /// the emitted code.</summary>
    /// <param name="value">The new stack pointer value.</param>
    public void SetStackPointer(int value) {
        EnsureCapacity(value);
        StackPointer = value;
    }

    /// <summary>Resets the stack pointer to 0 without clearing the backing array.
    /// Does not return the buffer to the pool — call <see cref="Dispose"/> for that.</summary>
    public void Reset() => StackPointer = 0;

    /// <summary>Returns the backing array to the shared <see cref="ArrayPool{long}"/>.
    /// After disposal the stack must not be used.</summary>
    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        ArrayPool<long>.Shared.Return(_slots, clearArray: true);
    }

    private void EnsureCapacity(int required) {
        if (required <= _slots.Length)
            return;
        var grown = Rent(Math.Max(required, _slots.Length * 2));
        Array.Copy(_slots, grown, StackPointer);
        var previous = _slots;
        _slots = grown;
        ArrayPool<long>.Shared.Return(previous, clearArray: true);
    }

    private static long[] Rent(int size) {
        var buffer = ArrayPool<long>.Shared.Rent(size);
        Array.Clear(buffer);
        return buffer;
    }
}
